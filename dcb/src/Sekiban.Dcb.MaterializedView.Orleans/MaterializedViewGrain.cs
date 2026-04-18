using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Streams;

namespace Sekiban.Dcb.MaterializedView.Orleans;

public sealed class MaterializedViewGrain : Grain, IMaterializedViewGrain
{
    // Global catch-up concurrency gate. Shared across all MaterializedViewGrain activations
    // in the current process/silo to protect the event store and MV relational store from
    // concurrent catch-up floods when many grains activate together.
    private static readonly SemaphoreSlim CatchUpBatchSemaphore = new(1, 1);
    private static int s_catchUpMaxConcurrentBatches = 1;

    private readonly IMvExecutor _executor;
    private readonly IMvApplyHostFactory _hostFactory;
    private readonly ILogger<MaterializedViewGrain> _logger;
    private readonly MvOptions _options;
    private readonly IMvRegistryStore _registryStore;
    private readonly IEventSubscriptionResolver _subscriptionResolver;

    private readonly List<SerializableEvent> _pendingStreamEvents = [];
    private IAsyncStream<SerializableEvent>? _stream;
    private StreamSubscriptionHandle<SerializableEvent>? _streamHandle;
    private bool _subscriptionStarting;
    private IDisposable? _catchUpTimer;
    private string? _grainKey;
    private string? _serviceId;
    private string? _viewName;
    private int _viewVersion;
    private IMvApplyHost? _host;
    private string? _lastAppliedSortableUniqueId;
    private string? _lastReceivedSortableUniqueId;
    private string? _lastError;
    private DateTimeOffset? _lastCatchUpStartedAt;
    private DateTimeOffset? _lastCatchUpCompletedAt;
    private bool _started;

    // Batch-driven catch-up orchestration state.
    private bool _isCatchUpActive;
    private bool _needsImmediateCatchUp;
    private bool _batchInFlight;
    private DateTimeOffset? _lastCatchUpAttemptAt;
    private int _consecutiveEmptyBatches;
    private string? _lastProgressSortableUniqueId;
    private long _catchUpBatchSkipCount;
    private bool _statusMarkedCatchingUp;

    public MaterializedViewGrain(
        IMvApplyHostFactory hostFactory,
        IMvExecutor executor,
        IMvRegistryStore registryStore,
        IEventSubscriptionResolver subscriptionResolver,
        IOptions<MvOptions> options,
        ILogger<MaterializedViewGrain> logger)
    {
        _hostFactory = hostFactory;
        _executor = executor;
        _registryStore = registryStore;
        _subscriptionResolver = subscriptionResolver;
        _logger = logger;
        _options = options.Value;
        ReconfigureCatchUpSemaphore(_options.CatchUpMaxConcurrentBatches);
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        ResolveIdentity();
        await PrepareStreamAsync();
        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _catchUpTimer?.Dispose();
        _catchUpTimer = null;
        if (_streamHandle is not null)
        {
            await _streamHandle.UnsubscribeAsync();
            _streamHandle = null;
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async Task EnsureStartedAsync()
    {
        if (_started)
        {
            return;
        }

        ResolveHost();
        await _executor.InitializeAsync(_host!, _serviceId, CancellationToken.None);
        await RefreshPositionFromRegistryAsync(CancellationToken.None);
        await StartSubscriptionAsync();

        _isCatchUpActive = true;
        _needsImmediateCatchUp = true;
        _consecutiveEmptyBatches = 0;
        _lastError = null;
        _lastCatchUpStartedAt = DateTimeOffset.UtcNow;
        _statusMarkedCatchingUp = false;
        StartCatchUpTimer();
        _started = true;
    }

    public async Task RefreshAsync()
    {
        await EnsureStartedAsync();

        // Activate catch-up for any callers that explicitly request a refresh.
        if (!_isCatchUpActive)
        {
            _isCatchUpActive = true;
            _needsImmediateCatchUp = true;
            _consecutiveEmptyBatches = 0;
            _statusMarkedCatchingUp = false;
            _lastCatchUpStartedAt = DateTimeOffset.UtcNow;
            StartCatchUpTimer();
        }

        // Drive catch-up to an idle/ready state synchronously for callers that
        // expect the grain to be fully caught up on return (preserves the
        // classic RefreshAsync semantics used by integration tests).
        // Orleans grain single-threading guarantees the scheduled timer will
        // not interleave with this loop inside the same grain.
        while (_isCatchUpActive)
        {
            var madeProgress = await RunCatchUpTickAsync(ignoreImmediateFlag: true, CancellationToken.None);
            if (!madeProgress && !_isCatchUpActive)
            {
                break;
            }

            if (!madeProgress)
            {
                break;
            }
        }

        await DrainPendingStreamEventsAsync(CancellationToken.None);
    }

    public async Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        await EnsureStartedAsync();
        if (string.IsNullOrWhiteSpace(sortableUniqueId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_lastAppliedSortableUniqueId))
        {
            await RefreshPositionFromRegistryAsync(CancellationToken.None);
        }

        return !string.IsNullOrWhiteSpace(_lastAppliedSortableUniqueId) &&
               string.Compare(_lastAppliedSortableUniqueId, sortableUniqueId, StringComparison.Ordinal) >= 0;
    }

    public Task<MaterializedViewGrainStatus> GetStatusAsync()
    {
        ResolveIdentity();
        return Task.FromResult(
            new MaterializedViewGrainStatus(
                _serviceId!,
                _viewName!,
                _viewVersion,
                _started,
                _batchInFlight,
                _streamHandle is not null,
                _pendingStreamEvents.Count,
                _lastAppliedSortableUniqueId,
                _lastReceivedSortableUniqueId,
                _lastError,
                _lastCatchUpStartedAt,
                _lastCatchUpCompletedAt,
                _isCatchUpActive,
                _lastCatchUpAttemptAt,
                _consecutiveEmptyBatches,
                _lastProgressSortableUniqueId,
                _needsImmediateCatchUp,
                _catchUpBatchSkipCount));
    }

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private async Task PrepareStreamAsync()
    {
        if (_stream is not null)
        {
            return;
        }

        var streamInfo = _subscriptionResolver.Resolve(_grainKey!);
        if (streamInfo is not OrleansSekibanStream orleansStream)
        {
            throw new InvalidOperationException(
                $"Materialized view grain requires Orleans stream subscription, but received '{streamInfo.GetType().Name}'.");
        }

        var streamProvider = this.GetStreamProvider(orleansStream.ProviderName);
        _stream = streamProvider.GetStream<SerializableEvent>(
            StreamId.Create(orleansStream.StreamNamespace, orleansStream.StreamId));
        await Task.CompletedTask;
    }

    private async Task StartSubscriptionAsync()
    {
        await PrepareStreamAsync();
        if (_streamHandle is not null || _stream is null || _subscriptionStarting)
        {
            return;
        }

        _subscriptionStarting = true;
        try
        {
            var observer = new StreamBatchObserver(this);
            var existing = await _stream.GetAllSubscriptionHandles();
            if (existing.Count > 0)
            {
                _streamHandle = await existing[0].ResumeAsync(observer);
                for (var i = 1; i < existing.Count; i++)
                {
                    await existing[i].UnsubscribeAsync();
                }
            }
            else
            {
                _streamHandle = await _stream.SubscribeAsync(observer, null);
            }
        }
        finally
        {
            _subscriptionStarting = false;
        }
    }

    private void StartCatchUpTimer()
    {
        if (_catchUpTimer is not null)
        {
            return;
        }

        _catchUpTimer = this.RegisterGrainTimer(
            _ => ProcessCatchUpTickAsync(),
            _options.PollInterval,
            _options.PollInterval);
    }

    private void StopCatchUpTimer()
    {
        _catchUpTimer?.Dispose();
        _catchUpTimer = null;
    }

    /// <summary>
    ///     Timer-driven tick. Runs at most one catch-up batch under the global
    ///     concurrency gate, then drains due buffered stream events.
    /// </summary>
    private async Task ProcessCatchUpTickAsync()
    {
        try
        {
            if (_isCatchUpActive)
            {
                RecoverStaleCatchUpIfNeeded();
                await RunCatchUpTickAsync(ignoreImmediateFlag: false, CancellationToken.None);
            }
            else
            {
                // Catch-up is idle: still drain buffered stream events whose
                // reorder window has elapsed.
                await DrainPendingStreamEventsAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning(
                ex,
                "Materialized view grain catch-up tick failed for {ViewName}/{ViewVersion}.",
                _viewName,
                _viewVersion);
        }
    }

    /// <summary>
    ///     Runs at most one catch-up batch. Returns true if the batch made any
    ///     progress (AppliedEvents &gt; 0), false if the batch was empty, skipped
    ///     due to the global gate, or catch-up is no longer active.
    /// </summary>
    private async Task<bool> RunCatchUpTickAsync(bool ignoreImmediateFlag, CancellationToken cancellationToken)
    {
        if (!_isCatchUpActive)
        {
            StopCatchUpTimer();
            return false;
        }

        if (_batchInFlight)
        {
            return false;
        }

        // Honour the immediate-catch-up flag only on the first tick after start;
        // subsequent ticks always run when the gate is available.
        if (!ignoreImmediateFlag && !_needsImmediateCatchUp && _lastCatchUpAttemptAt is { } lastAttempt &&
            DateTimeOffset.UtcNow - lastAttempt < _options.PollInterval.Divide(2))
        {
            // Not yet time for the next batch.
            return false;
        }

        var acquired = await CatchUpBatchSemaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
        if (!acquired)
        {
            _catchUpBatchSkipCount++;
            _logger.LogInformation(
                "Materialized view catch-up batch skipped due to global concurrency limit. View={ViewName}/{ViewVersion}, SkipCount={SkipCount}, PendingStreamEvents={PendingStreamEvents}, CurrentPosition={CurrentPosition}.",
                _viewName,
                _viewVersion,
                _catchUpBatchSkipCount,
                _pendingStreamEvents.Count,
                _lastAppliedSortableUniqueId ?? "beginning");
            return false;
        }

        _batchInFlight = true;
        _needsImmediateCatchUp = false;
        _lastCatchUpAttemptAt = DateTimeOffset.UtcNow;
        var madeProgress = false;

        try
        {
            if (!_statusMarkedCatchingUp)
            {
                await _registryStore.UpdateStatusAsync(
                    _serviceId!,
                    _host!.ViewName,
                    _host.ViewVersion,
                    MvStatus.CatchingUp,
                    cancellationToken: cancellationToken);
                _statusMarkedCatchingUp = true;
            }

            var result = await _executor.CatchUpOnceAsync(_host!, _serviceId, cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.LastAppliedSortableUniqueId))
            {
                _lastAppliedSortableUniqueId = result.LastAppliedSortableUniqueId;
                _lastProgressSortableUniqueId = result.LastAppliedSortableUniqueId;
            }

            if (result.AppliedEvents > 0)
            {
                _consecutiveEmptyBatches = 0;
                madeProgress = true;
            }
            else
            {
                _consecutiveEmptyBatches++;
            }

            var shouldSettle =
                result.ReachedUnsafeWindow ||
                _consecutiveEmptyBatches >= Math.Max(1, _options.MaxConsecutiveEmptyBatches);

            if (shouldSettle)
            {
                await CompleteCatchUpAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogError(
                ex,
                "Materialized view catch-up batch failed for {ViewName}/{ViewVersion}.",
                _viewName,
                _viewVersion);
            throw;
        }
        finally
        {
            _batchInFlight = false;
            CatchUpBatchSemaphore.Release();
        }

        // Regardless of catch-up result, drain due buffered stream events now
        // that the gate is released. Orleans grain single-threading keeps this
        // coordinated with subsequent ticks.
        await DrainPendingStreamEventsAsync(cancellationToken);

        return madeProgress;
    }

    private async Task CompleteCatchUpAsync(CancellationToken cancellationToken)
    {
        await RefreshPositionFromRegistryAsync(cancellationToken);
        await _registryStore.UpdateStatusAsync(
            _serviceId!,
            _host!.ViewName,
            _host.ViewVersion,
            MvStatus.Ready,
            cancellationToken: cancellationToken);
        _isCatchUpActive = false;
        _needsImmediateCatchUp = false;
        _consecutiveEmptyBatches = 0;
        _lastCatchUpCompletedAt = DateTimeOffset.UtcNow;
    }

    private void RecoverStaleCatchUpIfNeeded()
    {
        if (!_isCatchUpActive)
        {
            return;
        }

        var lastAttempt = _lastCatchUpAttemptAt ?? _lastCatchUpStartedAt;
        if (lastAttempt is null)
        {
            return;
        }

        if (DateTimeOffset.UtcNow - lastAttempt.Value <= _options.CatchUpStallThreshold)
        {
            return;
        }

        _logger.LogWarning(
            "Recovering stale materialized view catch-up state for {ViewName}/{ViewVersion}. LastAttemptAt={LastAttempt}, BatchInFlight={BatchInFlight}, PendingStreamEvents={PendingStreamEvents}.",
            _viewName,
            _viewVersion,
            lastAttempt,
            _batchInFlight,
            _pendingStreamEvents.Count);

        // Reset orchestration-only state. Registry state stays authoritative.
        _batchInFlight = false;
        _consecutiveEmptyBatches = 0;
        _needsImmediateCatchUp = true;
        _lastCatchUpAttemptAt = DateTimeOffset.UtcNow;
    }

    private async Task DrainPendingStreamEventsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var orderedBuffered = _pendingStreamEvents
                .GroupBy(serializableEvent => serializableEvent.SortableUniqueIdValue)
                .Select(group => group.First())
                .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
                .ToList();
            if (orderedBuffered.Count == 0)
            {
                return;
            }

            var thresholdUtc = DateTime.UtcNow - _options.StreamReorderWindow;
            var dueEvents = orderedBuffered
                .TakeWhile(serializableEvent => IsReadyToApply(serializableEvent, thresholdUtc))
                .ToList();
            if (dueEvents.Count == 0)
            {
                return;
            }

            var appliedSortableUniqueIds = new HashSet<string>(StringComparer.Ordinal);
            SerializableEvent? firstBlocked = null;

            foreach (var dueEvent in dueEvents)
            {
                var appliedEvents = await ApplyStreamEventsAsync([dueEvent], cancellationToken);
                if (appliedEvents > 0)
                {
                    appliedSortableUniqueIds.Add(dueEvent.SortableUniqueIdValue);
                    continue;
                }

                firstBlocked ??= dueEvent;
            }

            if (appliedSortableUniqueIds.Count == 0)
            {
                var blocked = firstBlocked ?? dueEvents[0];
                _logger.LogWarning(
                    "Materialized view grain stream apply made no progress for {ViewName}/{ViewVersion}. Pending={PendingCount}, FirstBlockedSortableUniqueId={SortableUniqueId}, FirstBlockedEventId={EventId}, FirstBlockedEventType={EventType}.",
                    _viewName,
                    _viewVersion,
                    _pendingStreamEvents.Count,
                    blocked.SortableUniqueIdValue,
                    blocked.Id,
                    blocked.EventPayloadName);
                return;
            }

            _pendingStreamEvents.RemoveAll(serializableEvent => appliedSortableUniqueIds.Contains(serializableEvent.SortableUniqueIdValue));
        }
    }

    internal async Task OnStreamBatchAsync(IEnumerable<SerializableEvent> events)
    {
        var batch = events
            .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();
        if (batch.Count == 0)
        {
            return;
        }

        var batchMaxSortableUniqueId = batch[^1].SortableUniqueIdValue;
        if (string.IsNullOrWhiteSpace(_lastReceivedSortableUniqueId) ||
            string.Compare(batchMaxSortableUniqueId, _lastReceivedSortableUniqueId, StringComparison.Ordinal) > 0)
        {
            _lastReceivedSortableUniqueId = batchMaxSortableUniqueId;
        }

        await _registryStore.MarkStreamReceivedAsync(
            _serviceId!,
            _viewName!,
            _viewVersion,
            batchMaxSortableUniqueId,
            DateTimeOffset.UtcNow,
            cancellationToken: CancellationToken.None);

        foreach (var serializableEvent in batch)
        {
            _pendingStreamEvents.Add(serializableEvent);
        }

        // Do not apply while a catch-up batch is executing or before the grain
        // is operational. The scheduled tick will drain buffered events once
        // the batch completes.
        if (_batchInFlight || !_started)
        {
            return;
        }

        await DrainPendingStreamEventsAsync(CancellationToken.None);
    }

    private async Task<int> ApplyStreamEventsAsync(IReadOnlyList<SerializableEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        ResolveHost();
        var applied = await _executor.ApplySerializableEventsAsync(_host!, events, _serviceId, cancellationToken);
        if (applied > 0)
        {
            await RefreshPositionFromRegistryAsync(cancellationToken);
        }

        return applied;
    }

    private async Task RefreshPositionFromRegistryAsync(CancellationToken cancellationToken)
    {
        ResolveHost();
        var entries = await _registryStore.GetEntriesAsync(_serviceId!, _host!.ViewName, _host.ViewVersion, cancellationToken);
        var currentPosition = entries
            .Select(entry => entry.CurrentPosition)
            .Where(position => !string.IsNullOrWhiteSpace(position))
            .OrderByDescending(position => position, StringComparer.Ordinal)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(currentPosition))
        {
            _lastAppliedSortableUniqueId = currentPosition;
        }
    }

    private static bool IsReadyToApply(SerializableEvent serializableEvent, DateTime thresholdUtc)
    {
        if (!SortableUniqueId.TryParse(serializableEvent.SortableUniqueIdValue, out var sortableUniqueId) ||
            sortableUniqueId is null)
        {
            return true;
        }

        return sortableUniqueId.GetDateTime() <= thresholdUtc;
    }

    private void ResolveIdentity()
    {
        if (!string.IsNullOrWhiteSpace(_grainKey))
        {
            return;
        }

        _grainKey = this.GetPrimaryKeyString();
        var (serviceId, viewName, viewVersion) = MvGrainKey.Parse(_grainKey);
        _serviceId = serviceId;
        _viewName = viewName;
        _viewVersion = viewVersion;
    }

    private void ResolveHost()
    {
        ResolveIdentity();
        if (_host is not null)
        {
            return;
        }

        _host = _hostFactory.Create(_viewName!, _viewVersion);
    }

    private static void ReconfigureCatchUpSemaphore(int desiredConcurrency)
    {
        var target = Math.Max(1, desiredConcurrency);
        var current = Volatile.Read(ref s_catchUpMaxConcurrentBatches);
        if (target == current)
        {
            return;
        }

        // The concurrency limit is process-wide. We only raise it here; the
        // effective bound is 1 by default, which matches the documented goal
        // of protecting the event store / relational store during backlog.
        if (target > current)
        {
            var delta = target - current;
            Interlocked.Exchange(ref s_catchUpMaxConcurrentBatches, target);
            CatchUpBatchSemaphore.Release(delta);
        }
    }

    private sealed class StreamBatchObserver : IAsyncBatchObserver<SerializableEvent>
    {
        private readonly MaterializedViewGrain _owner;

        public StreamBatchObserver(MaterializedViewGrain owner) => _owner = owner;

        public Task OnNextAsync(IList<SequentialItem<SerializableEvent>> items) =>
            _owner.OnStreamBatchAsync(items.Select(item => item.Item));

        public Task OnNextAsync(SerializableEvent item, StreamSequenceToken? token = null)
        {
            _ = token;
            return _owner.OnStreamBatchAsync([item]);
        }

        public Task OnNextBatchAsync(IEnumerable<SerializableEvent> batch, StreamSequenceToken? token = null)
        {
            _ = token;
            return _owner.OnStreamBatchAsync(batch);
        }

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex)
        {
            _owner._lastError = ex.Message;
            return Task.CompletedTask;
        }
    }
}
