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
    private readonly IMvExecutor _executor;
    private readonly ILogger<MaterializedViewGrain> _logger;
    private readonly MvOptions _options;
    private readonly IReadOnlyList<IMaterializedViewProjector> _projectors;
    private readonly IMvRegistryStore _registryStore;
    private readonly IEventSubscriptionResolver _subscriptionResolver;

    private bool _catchUpInProgress;
    private readonly List<SerializableEvent> _pendingStreamEvents = [];
    private IAsyncStream<SerializableEvent>? _stream;
    private StreamSubscriptionHandle<SerializableEvent>? _streamHandle;
    private bool _subscriptionStarting;
    private IDisposable? _catchUpTimer;
    private string? _grainKey;
    private string? _serviceId;
    private string? _viewName;
    private int _viewVersion;
    private IMaterializedViewProjector? _projector;
    private string? _lastAppliedSortableUniqueId;
    private string? _lastReceivedSortableUniqueId;
    private string? _lastError;
    private DateTimeOffset? _lastCatchUpStartedAt;
    private DateTimeOffset? _lastCatchUpCompletedAt;
    private bool _started;

    public MaterializedViewGrain(
        IEnumerable<IMaterializedViewProjector> projectors,
        IMvExecutor executor,
        IMvRegistryStore registryStore,
        IEventSubscriptionResolver subscriptionResolver,
        IOptions<MvOptions> options,
        ILogger<MaterializedViewGrain> logger)
    {
        _executor = executor;
        _registryStore = registryStore;
        _subscriptionResolver = subscriptionResolver;
        _logger = logger;
        _options = options.Value;
        _projectors = projectors.ToList();
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

        ResolveProjector();
        await _executor.InitializeAsync(_projector!, _serviceId, CancellationToken.None);
        await RefreshPositionFromRegistryAsync(CancellationToken.None);
        await StartSubscriptionAsync();
        await CatchUpAndDrainAsync(CancellationToken.None);

        _catchUpTimer ??= this.RegisterGrainTimer(
            _ => PeriodicBufferedApplyAsync(),
            _options.PollInterval,
            _options.PollInterval);
        _started = true;
    }

    public async Task RefreshAsync()
    {
        await EnsureStartedAsync();
        await CatchUpAndDrainAsync(CancellationToken.None);
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
                _catchUpInProgress,
                _streamHandle is not null,
                _pendingStreamEvents.Count,
                _lastAppliedSortableUniqueId,
                _lastReceivedSortableUniqueId,
                _lastError,
                _lastCatchUpStartedAt,
                _lastCatchUpCompletedAt));
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

    private async Task CatchUpAndDrainAsync(CancellationToken cancellationToken)
    {
        if (_catchUpInProgress)
        {
            return;
        }

        ResolveProjector();
        _catchUpInProgress = true;
        _lastCatchUpStartedAt = DateTimeOffset.UtcNow;
        _lastError = null;

        try
        {
            await _registryStore.UpdateStatusAsync(
                    _serviceId!,
                    _projector!.ViewName,
                    _projector.ViewVersion,
                    MvStatus.CatchingUp,
                    cancellationToken: cancellationToken)
                ;

            while (true)
            {
                var result = await _executor.CatchUpOnceAsync(_projector, _serviceId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(result.LastAppliedSortableUniqueId))
                {
                    _lastAppliedSortableUniqueId = result.LastAppliedSortableUniqueId;
                }

                if (result.AppliedEvents == 0 || result.ReachedUnsafeWindow)
                {
                    break;
                }
            }

            await DrainPendingStreamEventsAsync(cancellationToken);
            await RefreshPositionFromRegistryAsync(cancellationToken);
            await _registryStore.UpdateStatusAsync(
                    _serviceId!,
                    _projector.ViewName,
                    _projector.ViewVersion,
                    MvStatus.Ready,
                    cancellationToken: cancellationToken)
                ;
            _lastCatchUpCompletedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogError(
                ex,
                "Materialized view grain catch-up failed for {ViewName}/{ViewVersion}.",
                _viewName,
                _viewVersion);
            throw;
        }
        finally
        {
            _catchUpInProgress = false;
        }
    }

    private async Task DrainPendingStreamEventsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var orderedBuffered = _pendingStreamEvents
                .GroupBy(serializableEvent => serializableEvent.Id)
                .Select(group => group.OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal).Last())
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

            await ApplyStreamEventsAsync(dueEvents, cancellationToken);

            var dueIds = dueEvents.Select(serializableEvent => serializableEvent.Id).ToHashSet();
            _pendingStreamEvents.RemoveAll(serializableEvent => dueIds.Contains(serializableEvent.Id));
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
            if (!string.IsNullOrWhiteSpace(_lastAppliedSortableUniqueId) &&
                string.Compare(serializableEvent.SortableUniqueIdValue, _lastAppliedSortableUniqueId, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            _pendingStreamEvents.Add(serializableEvent);
        }

        if (_catchUpInProgress || !_started)
        {
            return;
        }

        await DrainPendingStreamEventsAsync(CancellationToken.None);
    }

    private async Task ApplyStreamEventsAsync(IReadOnlyList<SerializableEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        ResolveProjector();
        var applied = await _executor.ApplySerializableEventsAsync(_projector!, events, _serviceId, cancellationToken)
            ;
        if (applied > 0)
        {
            await RefreshPositionFromRegistryAsync(cancellationToken);
        }
    }

    private async Task RefreshPositionFromRegistryAsync(CancellationToken cancellationToken)
    {
        ResolveProjector();
        var entries = await _registryStore.GetEntriesAsync(_serviceId!, _projector!.ViewName, _projector.ViewVersion, cancellationToken)
            ;
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

    private async Task PeriodicBufferedApplyAsync()
    {
        if (_catchUpInProgress)
        {
            return;
        }

        try
        {
            await DrainPendingStreamEventsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning(
                ex,
                "Materialized view grain periodic buffered apply failed for {ViewName}/{ViewVersion}.",
                _viewName,
                _viewVersion);
        }
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

    private void ResolveProjector()
    {
        ResolveIdentity();
        if (_projector is not null)
        {
            return;
        }

        _projector = _projectors.SingleOrDefault(candidate =>
            string.Equals(candidate.ViewName, _viewName, StringComparison.Ordinal) &&
            candidate.ViewVersion == _viewVersion);
        if (_projector is null)
        {
            throw new InvalidOperationException(
                $"Materialized view projector '{_viewName}/{_viewVersion}' is not registered.");
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
