using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.MaterializedView.Orleans;

public sealed class MaterializedViewGrain : Grain, IMaterializedViewGrain
{
    // A provider restore request already has its own bounded transaction retry. This outer bound prevents a
    // conflict that persists across fresh catch-up ticks from becoming a permanent busy loop in the grain.
    private const int MaxActiveStatusRestoreAttempts = 3;

    // Global catch-up concurrency gate. Shared across all MaterializedViewGrain activations
    // in the current process/silo to protect the event store and MV relational store from
    // concurrent catch-up floods when many grains activate together.
    // Constructed with no maxCount so Release(delta) is safe when grains raise the limit
    // via MvOptions.CatchUpMaxConcurrentBatches. Narrowing is not supported.
    private static readonly SemaphoreSlim CatchUpBatchSemaphore = new(1);
    private static readonly object s_catchUpSemaphoreSync = new();
    private static int s_catchUpMaxConcurrentBatches = 1;
    // Test-only scheduler barrier. It is deliberately internal and has no production registration path: acceptance
    // tests use it to stop the current stream turn immediately after the durable receipt, before a catch-up timer can
    // apply anything, then let Orleans deactivate and reactivate the grain.
    private static Func<MaterializedViewGrain, bool>? s_afterStreamReceiptTestHook;

    private readonly IMvExecutor _executor;
    private readonly IMvApplyHostFactory _hostFactory;
    private readonly ILogger<MaterializedViewGrain> _logger;
    private readonly MvOptions _options;
    private readonly MvProjectionStatusPublisher? _statusPublisher;
    private readonly IMvRegistryStore _registryStore;
    private readonly IEventSubscriptionResolver _subscriptionResolver;

    // Stream payloads are wake-up hints only. Durable catch-up owns ordering and application; the grain retains only
    // bounded scalar receipt state so a burst cannot become an unbounded in-memory event queue.
    private int _pendingStreamHintCount;
    private string? _pendingStreamMaximumSortableUniqueId;
    private DateTimeOffset? _lastStreamHintReceivedAt;
    private IAsyncStream<SerializableEvent>? _stream;
    private StreamSubscriptionHandle<SerializableEvent>? _streamHandle;
    private bool _subscriptionStarting;
    private IDisposable? _catchUpTimer;
    private IDisposable? _statusTimer;
    private string? _grainKey;
    private string? _serviceId;
    private string? _viewName;
    private int _viewVersion;
    private IMvApplyHost? _host;
    private string? _lastAppliedSortableUniqueId;
    private MvProjectionStatusSnapshot _publicationSnapshot = MvProjectionStatusSnapshot.Unknown();
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
    private int _activeStatusRestoreAttempts;
    private int _consecutiveCatchUpFailures;
    private DateTimeOffset? _safeNoProgressSince;
    private string? _safeNoProgressPosition;
    private bool _catchUpHalted;
    private bool _needsLifecycleSettlement;
    private string? _settledEpochKey;
    private readonly MvCatchUpStallBudget _missingHintStallBudget;

    private MvModeCapabilities ResolveCapabilities(MvTransition transition)
    {
        ResolveIdentity();
        return MvModeCapabilities.ResolveAndValidate(
            _options,
            transition,
            new MvTransitionIdentity(_serviceId!, _viewName!, _viewVersion));
    }

    public MaterializedViewGrain(
        IMvApplyHostFactory hostFactory,
        IMvExecutor executor,
        IMvRegistryStore registryStore,
        IEventSubscriptionResolver subscriptionResolver,
        IOptions<MvOptions> options,
        ILogger<MaterializedViewGrain> logger)
        : this(hostFactory, executor, registryStore, subscriptionResolver, options, logger, statusPublisher: null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public MaterializedViewGrain(
        IMvApplyHostFactory hostFactory,
        IMvExecutor executor,
        IMvRegistryStore registryStore,
        IEventSubscriptionResolver subscriptionResolver,
        IOptions<MvOptions> options,
        ILogger<MaterializedViewGrain> logger,
        MvProjectionStatusPublisher? statusPublisher)
    {
        _hostFactory = hostFactory;
        _executor = executor;
        _registryStore = registryStore;
        _subscriptionResolver = subscriptionResolver;
        _logger = logger;
        _options = options.Value;
        _statusPublisher = statusPublisher;
        _missingHintStallBudget = new MvCatchUpStallBudget(_options.CatchUpStallThreshold);
        ReconfigureCatchUpSemaphore(_options.CatchUpMaxConcurrentBatches);
    }

    // Internal only so Orleans acceptance tests can invoke the real stream-hint path without changing the public
    // grain contract. The supplied key is parsed exactly as the Orleans primary-key path is parsed.
    internal MaterializedViewGrain(
        IMvApplyHostFactory hostFactory,
        IMvExecutor executor,
        IMvRegistryStore registryStore,
        IEventSubscriptionResolver subscriptionResolver,
        IOptions<MvOptions> options,
        ILogger<MaterializedViewGrain> logger,
        string testGrainKey)
        : this(hostFactory, executor, registryStore, subscriptionResolver, options, logger, statusPublisher: null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testGrainKey);
        var (serviceId, viewName, viewVersion) = MvGrainKey.Parse(testGrainKey);
        _grainKey = testGrainKey;
        _serviceId = ServiceIdValidator.NormalizeAndValidate(serviceId);
        _viewName = viewName;
        _viewVersion = viewVersion;
    }

    internal static IDisposable PushAfterStreamReceiptTestHook(Func<MaterializedViewGrain, bool> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        var previous = Interlocked.Exchange(ref s_afterStreamReceiptTestHook, hook);
        return new DelegateDisposable(() => Interlocked.Exchange(ref s_afterStreamReceiptTestHook, previous));
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        ResolveIdentity();
        _ = ResolveCapabilities(MvTransition.Initialize);
        await PrepareStreamAsync();
        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _catchUpTimer?.Dispose();
        _catchUpTimer = null;
        _statusTimer?.Dispose();
        _statusTimer = null;
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

        var capabilities = ResolveCapabilities(MvTransition.Initialize);
        ResolveHost();
        await _executor.InitializeAsync(_host!, _serviceId, CancellationToken.None);
        if (!capabilities.AllowsLifecycleDml)
        {
            // A verification-only lifecycle does not subscribe, capture a target, mark status, apply events, or let
            // a timer reach registry mutations after the schema gate succeeds.
            _started = true;
            return;
        }

        if (_executor is IMvActivationExecutor activationExecutor)
        {
            // Target capture is an explicit lifecycle step. Initialization creates registry rows only; it never
            // treats the absence of an active pointer as permission to cut over.
            await activationExecutor.CaptureTargetCheckpointAsync(_host!, _serviceId, CancellationToken.None);
        }

        await RefreshPositionFromRegistryAsync(CancellationToken.None);
        var servingActive = await _registryStore.GetActiveAsync(
                _serviceId!,
                _host!.ViewName,
                CancellationToken.None);
        if (servingActive?.ActiveVersion == _host.ViewVersion)
        {
            _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Active };
        }
        await StartSubscriptionAsync();

        _isCatchUpActive = true;
        _needsImmediateCatchUp = true;
        _consecutiveEmptyBatches = 0;
        _consecutiveCatchUpFailures = 0;
        _missingHintStallBudget.Reset();
        _safeNoProgressSince = null;
        _safeNoProgressPosition = null;
        _catchUpHalted = false;
        _needsLifecycleSettlement = true;
        _settledEpochKey = null;
        _activeStatusRestoreAttempts = 0;
        _lastError = null;
        _lastCatchUpStartedAt = DateTimeOffset.UtcNow;
        _statusMarkedCatchingUp = false;
        StartCatchUpTimer();
        _started = true;
        StartStatusTimer();
    }

    public async Task RefreshAsync()
    {
        var capabilities = ResolveCapabilities(MvTransition.Refresh);
        if (!capabilities.AllowsLifecycleDml)
        {
            throw MvModeCapabilities.CreateRefusal(
                _options.InitializationMode,
                MvTransition.Refresh,
                new MvTransitionIdentity(_serviceId!, _viewName!, _viewVersion));
        }
        await EnsureStartedAsync();

        // Refresh is the explicit operator/user boundary that is allowed to restart a halted or exhausted cycle.
        _catchUpHalted = false;
        _lastError = null;
        _consecutiveCatchUpFailures = 0;
        _missingHintStallBudget.Reset();
        _safeNoProgressSince = null;
        _safeNoProgressPosition = null;
        _needsLifecycleSettlement = true;
        _settledEpochKey = null;

        // Activate catch-up for any callers that explicitly request a refresh.
        if (!_isCatchUpActive)
        {
            _isCatchUpActive = true;
            _needsImmediateCatchUp = true;
            _consecutiveEmptyBatches = 0;
            _consecutiveCatchUpFailures = 0;
            _catchUpHalted = false;
            _safeNoProgressSince = null;
            _safeNoProgressPosition = null;
            _needsLifecycleSettlement = true;
            _activeStatusRestoreAttempts = 0;
            _statusMarkedCatchingUp = false;
            _lastCatchUpStartedAt = DateTimeOffset.UtcNow;
            StartCatchUpTimer();
        }

        // Drive catch-up to an idle/ready state synchronously for callers that
        // expect the grain to be fully caught up on return (preserves the
        // classic RefreshAsync semantics used by integration tests).
        // Orleans grain single-threading guarantees the scheduled timer will
        // not interleave with this loop inside the same grain.
        //
        // A tick may return madeProgress=false because (a) catch-up settled
        // in that tick, (b) the global concurrency gate was busy, or (c)
        // another grain turn has a batch in flight. Only (a) means "done";
        // for (b) and (c) we must wait briefly and retry, otherwise
        // RefreshAsync returns while catch-up is still active.
        var settleDeadline = DateTime.UtcNow + _options.CatchUpStallThreshold;
        while (_isCatchUpActive)
        {
            var madeProgress = await RunCatchUpTickAsync(ignoreImmediateFlag: true, CancellationToken.None);
            if (!_isCatchUpActive)
            {
                break;
            }

            if (!madeProgress)
            {
                if (DateTime.UtcNow >= settleDeadline)
                {
                    _logger.LogWarning(
                        "RefreshAsync timed out waiting for catch-up to settle for {ViewName}/{ViewVersion}. IsCatchUpActive={IsCatchUpActive}, BatchInFlight={BatchInFlight}.",
                        _viewName,
                        _viewVersion,
                        _isCatchUpActive,
                        _batchInFlight);
                    break;
                }

                // A pending receipt whose durable event is not visible yet must not turn RefreshAsync into a tight
                // retry loop. Gate/in-flight contention keeps the short back-off; an outstanding hint uses the poll
                // interval as the bounded provider-read cadence.
                var retryDelay = _pendingStreamHintCount > 0 && _options.PollInterval > TimeSpan.Zero
                    ? _options.PollInterval
                    : TimeSpan.FromMilliseconds(10);
                await Task.Delay(retryDelay, CancellationToken.None);
            }
        }

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
                _isCatchUpActive || _batchInFlight,
                _streamHandle is not null,
                _pendingStreamHintCount,
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
                _catchUpBatchSkipCount)
            {
                CatchUpHalted = _catchUpHalted
            });
    }

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private Task PrepareStreamAsync()
    {
        if (_stream is not null)
        {
            return Task.CompletedTask;
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
        return Task.CompletedTask;
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

        // Start the first tick immediately so initial backlog catch-up does
        // not wait for an entire PollInterval before beginning. This also
        // makes NeedsImmediateCatchUp effective on the very first cycle.
        _catchUpTimer = this.RegisterGrainTimer(
            _ => ProcessCatchUpTickAsync(),
            TimeSpan.Zero,
            _options.PollInterval);
    }

    private void StartStatusTimer()
    {
        if (_statusTimer is not null || _statusPublisher is null)
        {
            return;
        }

        _statusTimer = this.RegisterGrainTimer(
            cancellationToken => PublishStatusAsync(cancellationToken),
            TimeSpan.Zero,
            _statusPublisher.PublicationInterval);
    }

    private async Task PublishStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _statusPublisher!.PublishIfDueAsync(
                    _serviceId!,
                    _viewName!,
                    _viewVersion,
                    _publicationSnapshot,
                    MvProjectionStatusPublisherKind.Orleans,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Timer disposal/deactivation cancellation is normal.
        }
    }

    private void StopCatchUpTimer()
    {
        _catchUpTimer?.Dispose();
        _catchUpTimer = null;
    }

    /// <summary>Timer-driven tick. Stream notifications only wake durable catch-up; they never apply payloads inline.</summary>
    private async Task ProcessCatchUpTickAsync()
    {
        try
        {
            if (_isCatchUpActive)
            {
                RecoverStaleCatchUpIfNeeded();
                await RunCatchUpTickAsync(ignoreImmediateFlag: false, CancellationToken.None);
            }
            else if (ShouldRunIdleDurableProbe())
            {
                // A stream notification can be lost after the durable receipt marker is committed. Keep a bounded
                // provider poll alive after settlement so the store, not the stream payload, is the source of truth.
                _isCatchUpActive = true;
                _needsImmediateCatchUp = true;
                _needsLifecycleSettlement = false;
                await RunCatchUpTickAsync(ignoreImmediateFlag: true, CancellationToken.None);
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

    private bool ShouldRunIdleDurableProbe()
    {
        if (!_started || _catchUpHalted || _batchInFlight)
        {
            return false;
        }

        if (_pendingStreamHintCount > 0)
        {
            return true;
        }

        var pollInterval = _options.PollInterval > TimeSpan.Zero ? _options.PollInterval : TimeSpan.Zero;
        var safeWindow = _options.SafeWindowMs > 0
            ? TimeSpan.FromMilliseconds(_options.SafeWindowMs)
            : TimeSpan.Zero;
        var minimumInterval = pollInterval > safeWindow ? pollInterval : safeWindow;
        return _lastCatchUpAttemptAt is null || DateTimeOffset.UtcNow - _lastCatchUpAttemptAt >= minimumInterval;
    }

    /// <summary>
    ///     Runs at most one durable catch-up batch. Returns true if the batch made any
    ///     progress (AppliedEvents &gt; 0), false if the batch was empty, skipped
    ///     due to the global gate, or catch-up is no longer active.
    /// </summary>
    private async Task<bool> RunCatchUpTickAsync(bool ignoreImmediateFlag, CancellationToken cancellationToken)
    {
        var capabilities = ResolveCapabilities(MvTransition.CatchUp);
        if (!capabilities.AllowsProjectorApply || !capabilities.AllowsLifecycleDml)
        {
            return false;
        }

        if (!_isCatchUpActive)
        {
            StopCatchUpTimer();
            return false;
        }

        if (_batchInFlight)
        {
            return false;
        }

        // The immediate-catch-up flag is honoured only on the first tick after
        // startup. On later ticks we run whenever the gate is available.
        if (!ignoreImmediateFlag && !_needsImmediateCatchUp && _lastCatchUpAttemptAt is { } lastAttempt &&
            _options.PollInterval > TimeSpan.Zero &&
            DateTimeOffset.UtcNow - lastAttempt < _options.PollInterval)
        {
            // Not yet time for the next batch.
            return false;
        }

        var acquired = await CatchUpBatchSemaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
        if (!acquired)
        {
            _catchUpBatchSkipCount++;
            _logger.LogInformation(
                "Materialized view catch-up batch skipped due to global concurrency limit. View={ViewName}/{ViewVersion}, SkipCount={SkipCount}, PendingStreamHints={PendingStreamHints}, CurrentPosition={CurrentPosition}.",
                _viewName,
                _viewVersion,
                _catchUpBatchSkipCount,
                _pendingStreamHintCount,
                _lastAppliedSortableUniqueId ?? "beginning");
            return false;
        }

        _batchInFlight = true;
        _needsImmediateCatchUp = false;
        _lastCatchUpAttemptAt = DateTimeOffset.UtcNow;
        var consumedStreamHint = ConsumePendingStreamHints();
        var madeProgress = false;
        var servingActive = false;
        long? servingGeneration = null;

        try
        {
            if (!_statusMarkedCatchingUp)
            {
                var active = await _registryStore.GetActiveAsync(
                        _serviceId!,
                        _host!.ViewName,
                        cancellationToken);
                servingActive = active?.ActiveVersion == _host.ViewVersion;
                servingGeneration = servingActive ? active!.Generation : null;
                if (!servingActive)
                {
                    await _registryStore.UpdateStatusAsync(
                        _serviceId!,
                        _host.ViewName,
                        _host.ViewVersion,
                        MvStatus.CatchingUp,
                        cancellationToken: cancellationToken);
                }

                _statusMarkedCatchingUp = true;
                if (servingActive)
                {
                    _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Active };
                }
                else
                {
                    _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.CatchingUp };
                }
            }
            else
            {
                var active = await _registryStore.GetActiveAsync(
                        _serviceId!,
                        _host!.ViewName,
                        cancellationToken);
                servingActive = active?.ActiveVersion == _host.ViewVersion;
                servingGeneration = servingActive ? active!.Generation : null;
            }

            var result = _executor is IMvOrleansCatchUpExecutor orleansCatchUpExecutor
                ? await orleansCatchUpExecutor.CatchUpOnceForOrleansAsync(_host!, _serviceId, cancellationToken)
                : await _executor.CatchUpOnceAsync(_host!, _serviceId, cancellationToken);
            var activeAfterBatch = await _registryStore.GetActiveAsync(
                    _serviceId!,
                    _host!.ViewName,
                    cancellationToken);
            servingActive = servingActive &&
                activeAfterBatch?.ActiveVersion == _host.ViewVersion &&
                servingGeneration == activeAfterBatch.Generation;
            var settlementEpochChanged = _settledEpochKey is not null &&
                await HasSettledEpochChangedAsync(cancellationToken);
            if (result.ProjectionStatus is { } projectionStatus)
            {
                _publicationSnapshot = projectionStatus;
            }
            if (servingActive)
            {
                // The serving version remains Active while refresh catches up. The provider restore boundary below
                // repairs any stale lifecycle rows once the batch reaches its settle point.
                _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Active };
            }

            if (!string.IsNullOrWhiteSpace(result.LastAppliedSortableUniqueId))
            {
                _lastAppliedSortableUniqueId = result.LastAppliedSortableUniqueId;
                _lastProgressSortableUniqueId = result.LastAppliedSortableUniqueId;
            }

            if (result.IsFailure)
            {
                RequeueStreamHint(consumedStreamHint);
                _missingHintStallBudget.PauseAfterFailure();
                _consecutiveCatchUpFailures++;
                _lastError = result.ErrorMessage ?? $"Materialized view catch-up failed ({result.Outcome}).";
                _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Faulted };
                if (!result.IsRetryable || result.Outcome == MvCatchUpOutcome.PermanentUnsupported ||
                    _consecutiveCatchUpFailures >= Math.Max(1, _options.MaxConsecutiveFailuresBeforeStop))
                {
                    HaltCatchUp(_lastError);
                }

                return false;
            }

            _consecutiveCatchUpFailures = 0;
            if (result.AppliedEvents > 0)
            {
                _consecutiveEmptyBatches = 0;
                madeProgress = true;
                _safeNoProgressSince = null;
                _safeNoProgressPosition = null;
                _missingHintStallBudget.Reset();
            }
            else if (result.Outcome == MvCatchUpOutcome.NoProgress)
            {
                if (RecordSafeNoProgress())
                {
                    HaltCatchUp(
                        $"Materialized view catch-up made no safe progress at {_lastAppliedSortableUniqueId ?? "the beginning"} for {_options.CatchUpStallThreshold}; observed at {DateTimeOffset.UtcNow:O}.");
                }
            }
            else if (result.Outcome == MvCatchUpOutcome.Empty)
            {
                _consecutiveEmptyBatches++;
            }

            if (settlementEpochChanged)
            {
                // A changed target, current truth, lifecycle status, active version, or generation invalidates the
                // previous completion epoch. The next empty/unsafe observation must pass through the real settlement
                // boundary again instead of reusing an unchanged-idle shortcut.
                _needsLifecycleSettlement = true;
            }

            var newerHintOutstanding = IsNewerThanCurrent(consumedStreamHint, result);
            var hintSafeEligible = MvCatchUpStallBudget.IsSafeEligible(
                consumedStreamHint,
                _options.SafeWindowMs,
                DateTimeOffset.UtcNow);
            if (_missingHintStallBudget.Observe(
                    consumedStreamHint,
                    newerHintOutstanding,
                    result.AppliedEvents,
                    result.Outcome,
                    hintSafeEligible))
            {
                var stalledHint = _missingHintStallBudget.HintSortableUniqueId ?? consumedStreamHint ?? "unknown";
                var firstObservedAt = _missingHintStallBudget.FirstObservedAtUtc ?? DateTimeOffset.UtcNow;
                HaltCatchUp(
                    $"Materialized view catch-up could not make safe progress for outstanding hint {stalledHint}; " +
                    $"first observed at {firstObservedAt:O}, exhausted {_options.CatchUpStallThreshold} at {DateTimeOffset.UtcNow:O}.");
            }

            if (newerHintOutstanding &&
                (result.Outcome is MvCatchUpOutcome.Empty or MvCatchUpOutcome.UnsafeWindow || result.ReachedUnsafeWindow))
            {
                // Do not let an empty/unsafe read settle a receipt that is newer than the durable position observed by
                // this tick. Keep one scalar wake-up marker; the next poll retries without retaining its payload.
                RequeueStreamHint(consumedStreamHint);
            }

            var shouldSettle =
                ((result.ReachedUnsafeWindow || result.Outcome == MvCatchUpOutcome.UnsafeWindow) && !newerHintOutstanding) ||
                (result.Outcome == MvCatchUpOutcome.Empty &&
                 _consecutiveEmptyBatches >= Math.Max(1, _options.MaxConsecutiveEmptyBatches) &&
                 !newerHintOutstanding);

            if (shouldSettle && _isCatchUpActive && _needsLifecycleSettlement)
            {
                await CompleteCatchUpAsync(cancellationToken);
            }
            else if (shouldSettle && _isCatchUpActive)
            {
                // Neither hint re-entry nor an idle durable probe is a lifecycle settlement boundary. A duplicate or
                // unchanged store observation must not perform a second guarded restore.
                _isCatchUpActive = false;
                _needsImmediateCatchUp = false;
            }
        }
        catch (Exception ex)
        {
            RequeueStreamHint(consumedStreamHint);
            _missingHintStallBudget.PauseAfterFailure();
            _lastError = ex.Message;
            _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Faulted };
            _consecutiveCatchUpFailures++;
            _logger.LogError(
                ex,
                "Materialized view catch-up batch failed for {ViewName}/{ViewVersion}.",
                _viewName,
                _viewVersion);
            if (_consecutiveCatchUpFailures >= Math.Max(1, _options.MaxConsecutiveFailuresBeforeStop))
            {
                HaltCatchUp(_lastError);
            }
        }
        finally
        {
            _batchInFlight = false;
            CatchUpBatchSemaphore.Release();
        }

        return madeProgress;
    }

    private async Task CompleteCatchUpAsync(CancellationToken cancellationToken)
    {
        var capabilities = ResolveCapabilities(MvTransition.CatchUp);
        if (!capabilities.AllowsLifecycleDml)
        {
            return;
        }

        await RefreshPositionFromRegistryAsync(cancellationToken);

        // Registry truth is the readiness gate. A legacy row, failed read, or
        // malformed/unknown checkpoint must never be promoted to Ready just
        // because the executor returned an empty batch.
        var entries = await _registryStore.GetEntriesAsync(
            _serviceId!,
            _host!.ViewName,
            _host.ViewVersion,
            cancellationToken);
        _publicationSnapshot = MvProjectionStatusSnapshot.FromEntries(entries);
        if (entries.Count == 0 || entries.Any(entry => !entry.CurrentCheckpointTruth.IsKnown))
        {
            _lastError = "Materialized view checkpoint truth is Unknown; readiness remains fail-closed.";
            _consecutiveEmptyBatches = 0;
            return;
        }

        if (_executor is IMvActivationExecutor activationExecutor)
        {
            if (entries.Any(entry =>
                    entry.Status == MvStatus.Faulted ||
                    !entry.TargetCheckpointTruth.IsKnown ||
                    entry.TargetCheckpointTruth.Provenance?.Kind != MvCheckpointProvenanceKind.AuthoritativeTargetCapture ||
                    !entry.CurrentCheckpointTruth.Satisfies(entry.TargetCheckpointTruth)))
            {
                _lastError = "Materialized view candidate is not at its authoritative target; readiness remains fail-closed.";
                _consecutiveEmptyBatches = 0;
                return;
            }

            var active = await _registryStore.GetActiveAsync(
                    _serviceId!,
                    _host!.ViewName,
                    cancellationToken);
            if (active?.ActiveVersion == _host.ViewVersion)
            {
                var restoreRequest = MvActiveStatusRestoreRequest.FromEntries(
                    _serviceId!,
                    _host.ViewName,
                    _host.ViewVersion,
                    active.Generation,
                    entries);
                MvActivationResult restoration;
                try
                {
                    restoration = await _registryStore.TryRestoreActiveStatusAsync(
                            restoreRequest,
                            cancellationToken: cancellationToken);
                }
                catch (NotSupportedException ex)
                {
                    EndCatchUpAfterActiveStatusRestoreFailure(
                        $"Serving materialized-view status restoration is unsupported: {ex.Message}");
                    return;
                }

                if (!restoration.Succeeded)
                {
                    var retryable = IsRetryableActiveStatusRestore(restoration);
                    var attempt = ++_activeStatusRestoreAttempts;
                    var error =
                        $"Serving materialized-view status restoration was rejected: {restoration.FailureReason}. {restoration.Message}";
                    if (retryable && attempt < MaxActiveStatusRestoreAttempts)
                    {
                        _lastError =
                            $"{error} Retrying from a fresh catch-up boundary (attempt {attempt}/{MaxActiveStatusRestoreAttempts}).";
                        _consecutiveEmptyBatches = 0;
                        return;
                    }

                    if (retryable && restoration.FailureReason != MvActivationFailureReason.RetryExhausted)
                    {
                        error =
                            $"{error} Outer catch-up retry bound exhausted after {attempt} attempts.";
                    }

                    EndCatchUpAfterActiveStatusRestoreFailure(error);
                    return;
                }

                _activeStatusRestoreAttempts = 0;
                _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Active };
                _isCatchUpActive = false;
                _needsImmediateCatchUp = false;
                _needsLifecycleSettlement = false;
                _consecutiveEmptyBatches = 0;
                _lastCatchUpCompletedAt = DateTimeOffset.UtcNow;
                await CaptureSettledEpochAsync(cancellationToken);
                return;
            }

            if (entries.Any(entry => entry.Status is not (MvStatus.CatchingUp or MvStatus.Ready)))
            {
                _lastError = "Materialized view candidate lifecycle is not eligible for activation.";
                _consecutiveEmptyBatches = 0;
                return;
            }

            await _registryStore.UpdateStatusAsync(
                    _serviceId!,
                    _host.ViewName,
                    _host.ViewVersion,
                    MvStatus.Ready,
                    cancellationToken: cancellationToken);
            _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Ready };

            var activation = await activationExecutor.TryActivateAsync(
                    _host,
                    _serviceId,
                    cancellationToken);
            if (!activation.Succeeded && activation.FailureReason != MvActivationFailureReason.AlreadyActive)
            {
                _lastError = $"Materialized view activation was rejected: {activation.FailureReason}.";
                _consecutiveEmptyBatches = 0;
                return;
            }

            _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Active };
            _isCatchUpActive = false;
            _needsImmediateCatchUp = false;
            _needsLifecycleSettlement = false;
            _consecutiveEmptyBatches = 0;
            _lastCatchUpCompletedAt = DateTimeOffset.UtcNow;
            await CaptureSettledEpochAsync(cancellationToken);
            return;
        }

        await _registryStore.UpdateStatusAsync(
            _serviceId!,
            _host!.ViewName,
            _host.ViewVersion,
            MvStatus.Ready,
            cancellationToken: cancellationToken);
        _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Ready };
        _isCatchUpActive = false;
        _needsImmediateCatchUp = false;
        _needsLifecycleSettlement = false;
        _consecutiveEmptyBatches = 0;
        _lastCatchUpCompletedAt = DateTimeOffset.UtcNow;
        await CaptureSettledEpochAsync(cancellationToken);
    }

    private static bool IsRetryableActiveStatusRestore(MvActivationResult restoration) =>
        restoration.FailureReason != MvActivationFailureReason.RetryExhausted &&
        (restoration.IsRetryableConcurrency ||
         restoration.FailureReason is MvActivationFailureReason.ExpectedActiveConflict or MvActivationFailureReason.ExpectedGenerationConflict);

    private void EndCatchUpAfterActiveStatusRestoreFailure(string error)
    {
        HaltCatchUp(error);
    }

    private bool RecordSafeNoProgress()
    {
        var currentPosition = _lastAppliedSortableUniqueId;
        if (!string.Equals(_safeNoProgressPosition, currentPosition, StringComparison.Ordinal))
        {
            _safeNoProgressPosition = currentPosition;
            _safeNoProgressSince = DateTimeOffset.UtcNow;
            return false;
        }

        _safeNoProgressSince ??= DateTimeOffset.UtcNow;
        return DateTimeOffset.UtcNow - _safeNoProgressSince >= _options.CatchUpStallThreshold;
    }

    private void HaltCatchUp(string error)
    {
        _lastError = error;
        _publicationSnapshot = _publicationSnapshot with { Status = MvStatus.Faulted };
        _catchUpHalted = true;
        _isCatchUpActive = false;
        _needsImmediateCatchUp = false;
        _needsLifecycleSettlement = false;
        _consecutiveEmptyBatches = 0;
        _safeNoProgressSince = null;
        _safeNoProgressPosition = null;
        _missingHintStallBudget.Reset();
        StopCatchUpTimer();
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

        // If a batch is still truly in-flight (the grain is yielded on an
        // awaited DB/executor call), leave _batchInFlight alone. The batch's
        // finally block will release the semaphore and clear the flag.
        // Clearing _batchInFlight here would let OnStreamBatchAsync drain
        // stream events while catch-up is still writing to the MV store,
        // breaking single-flight ordering. If the underlying task is hung,
        // the grain is hung regardless, so clearing the flag would not help.
        if (_batchInFlight)
        {
            _logger.LogWarning(
                "Materialized view catch-up appears stalled for {ViewName}/{ViewVersion} but a batch is still marked in-flight; leaving single-flight state intact. LastAttemptAt={LastAttempt}, PendingStreamHints={PendingStreamHints}.",
                _viewName,
                _viewVersion,
                lastAttempt,
                _pendingStreamHintCount);
            _lastCatchUpAttemptAt = DateTimeOffset.UtcNow;
            return;
        }

        _logger.LogWarning(
            "Recovering stale materialized view catch-up state for {ViewName}/{ViewVersion}. LastAttemptAt={LastAttempt}, PendingStreamHints={PendingStreamHints}.",
            _viewName,
            _viewVersion,
            lastAttempt,
            _pendingStreamHintCount);

        // Reset orchestration-only state. Registry state stays authoritative.
        _consecutiveEmptyBatches = 0;
        _needsImmediateCatchUp = true;
        _lastCatchUpAttemptAt = DateTimeOffset.UtcNow;
    }

    internal async Task OnStreamBatchAsync(IEnumerable<SerializableEvent> events)
    {
        var capabilities = ResolveCapabilities(MvTransition.Apply);
        if (!capabilities.AllowsProjectorApply)
        {
            return;
        }

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

        _pendingStreamHintCount = (int)Math.Min(int.MaxValue, (long)_pendingStreamHintCount + batch.Count);
        if (string.IsNullOrWhiteSpace(_pendingStreamMaximumSortableUniqueId) ||
            string.Compare(batchMaxSortableUniqueId, _pendingStreamMaximumSortableUniqueId, StringComparison.Ordinal) > 0)
        {
            _pendingStreamMaximumSortableUniqueId = batchMaxSortableUniqueId;
        }
        _lastStreamHintReceivedAt = DateTimeOffset.UtcNow;

        var afterReceiptHook = Volatile.Read(ref s_afterStreamReceiptTestHook);
        if (afterReceiptHook?.Invoke(this) == true)
        {
            return;
        }

        // A receipt is durable even when the stream callback arrives before startup or while a batch is in flight.
        // The next bounded store read discovers events in SUID order. A permanently halted grain remains halted until
        // an explicit RefreshAsync or a fresh activation, so a bad store cannot become a busy loop.
        if (!_started || _catchUpHalted)
        {
            return;
        }

        if (!_isCatchUpActive)
        {
            _isCatchUpActive = true;
            _needsImmediateCatchUp = true;
            // Re-entering catch-up because of a stream hint does not itself invalidate the settled lifecycle epoch.
            // A genuinely newer durable event (or another registry mutation) is detected after the store read by
            // HasSettledEpochChangedAsync. Keeping this false prevents an already-applied duplicate hint from
            // repeating TryRestoreActiveStatusAsync while preserving the real invalidation path.
            _consecutiveEmptyBatches = 0;
            _activeStatusRestoreAttempts = 0;
            _lastCatchUpStartedAt ??= DateTimeOffset.UtcNow;
            StartCatchUpTimer();
        }
    }

    private string? ConsumePendingStreamHints()
    {
        var consumedMaximum = _pendingStreamMaximumSortableUniqueId;
        if (_pendingStreamHintCount > 0 && !string.IsNullOrWhiteSpace(_pendingStreamMaximumSortableUniqueId))
        {
            _lastReceivedSortableUniqueId = _pendingStreamMaximumSortableUniqueId;
        }

        _pendingStreamHintCount = 0;
        _pendingStreamMaximumSortableUniqueId = null;
        _lastStreamHintReceivedAt = null;
        return consumedMaximum;
    }

    private void RequeueStreamHint(string? sortableUniqueId)
    {
        if (string.IsNullOrWhiteSpace(sortableUniqueId))
        {
            return;
        }

        _pendingStreamHintCount = (int)Math.Min(int.MaxValue, (long)_pendingStreamHintCount + 1);
        if (string.IsNullOrWhiteSpace(_pendingStreamMaximumSortableUniqueId) ||
            string.Compare(sortableUniqueId, _pendingStreamMaximumSortableUniqueId, StringComparison.Ordinal) > 0)
        {
            _pendingStreamMaximumSortableUniqueId = sortableUniqueId;
        }
    }

    private bool IsNewerThanCurrent(string? sortableUniqueId, MvCatchUpResult result)
    {
        if (string.IsNullOrWhiteSpace(sortableUniqueId))
        {
            return false;
        }

        var currentPosition = result.ProjectionStatus?.CurrentCheckpointTruth is { IsKnown: true } truth
            ? truth.PositionValue
            : _lastAppliedSortableUniqueId;
        return string.IsNullOrWhiteSpace(currentPosition) ||
               string.Compare(sortableUniqueId, currentPosition, StringComparison.Ordinal) > 0;
    }

    private async Task<bool> HasSettledEpochChangedAsync(CancellationToken cancellationToken)
    {
        var entries = await _registryStore.GetEntriesAsync(
                _serviceId!,
                _host!.ViewName,
                _host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var active = await _registryStore.GetActiveAsync(
                _serviceId!,
                _host.ViewName,
                cancellationToken)
            .ConfigureAwait(false);
        return !string.Equals(_settledEpochKey, CreateEpochKey(entries, active), StringComparison.Ordinal);
    }

    private async Task CaptureSettledEpochAsync(CancellationToken cancellationToken)
    {
        var entries = await _registryStore.GetEntriesAsync(
                _serviceId!,
                _host!.ViewName,
                _host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var active = await _registryStore.GetActiveAsync(
                _serviceId!,
                _host.ViewName,
                cancellationToken)
            .ConfigureAwait(false);
        _settledEpochKey = CreateEpochKey(entries, active);
    }

    // Internal only so the deterministic acceptance tests can exercise the exact epoch fence without waiting for
    // timer-driven Orleans work. This is not part of the public grain or package surface.
    internal static string CreateEpochKey(
        IReadOnlyList<MvRegistryEntry> entries,
        MvActiveEntry? active)
    {
        var activeKey = active is null
            ? "none"
            : $"{active.ActiveVersion}:{active.Generation}";
        var entryKey = string.Join(
            ";",
            entries
                .OrderBy(entry => entry.LogicalTable, StringComparer.Ordinal)
                .Select(entry => string.Join(
                    "|",
                    entry.LogicalTable,
                    entry.Status,
                    MvCheckpointTruthCodec.Encode(entry.CurrentCheckpointTruth),
                    MvCheckpointTruthCodec.Encode(entry.TargetCheckpointTruth))));
        return $"{activeKey};{entryKey}";
    }

    private async Task RefreshPositionFromRegistryAsync(CancellationToken cancellationToken)
    {
        ResolveHost();
        var capabilities = ResolveCapabilities(MvTransition.VerifyInitialization);
        var entries = capabilities.UsesReadOnlyInspection && _registryStore is IMvReadOnlyMvInspector inspector
            ? await inspector.ReadRegistryEntriesAsync(_serviceId!, _host!.ViewName, _host.ViewVersion, cancellationToken)
            : await _registryStore.GetEntriesAsync(_serviceId!, _host!.ViewName, _host.ViewVersion, cancellationToken);
        _publicationSnapshot = MvProjectionStatusSnapshot.FromEntries(entries);
        var currentPosition = entries
            .Select(entry => entry.CurrentCheckpointTruth.IsKnown ? entry.CurrentCheckpointTruth.PositionValue : null)
            .Where(position => !string.IsNullOrWhiteSpace(position))
            .OrderByDescending(position => position, StringComparer.Ordinal)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(currentPosition))
        {
            _lastAppliedSortableUniqueId = currentPosition;
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
        var normalizedServiceId = ServiceIdValidator.NormalizeAndValidate(serviceId);
        if (string.Equals(normalizedServiceId, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal) &&
            !_options.AllowDefaultServiceId)
        {
            throw new InvalidOperationException(
                "MaterializedViewGrain cannot use the implicit default ServiceId. Set AllowDefaultServiceId only for an explicit single-service compatibility registration.");
        }

        if (!string.IsNullOrWhiteSpace(_options.ServiceId))
        {
            var configuredServiceId = ServiceIdValidator.NormalizeAndValidate(_options.ServiceId);
            if (!string.Equals(configuredServiceId, normalizedServiceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"MaterializedViewGrain key ServiceId '{normalizedServiceId}' does not match configured ServiceId '{configuredServiceId}'.");
            }
        }

        _serviceId = normalizedServiceId;
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
        if (Volatile.Read(ref s_catchUpMaxConcurrentBatches) >= target)
        {
            return;
        }

        // The concurrency limit is process-wide. We only raise it here; the
        // effective bound is 1 by default, which matches the documented goal
        // of protecting the event store / relational store during backlog.
        // The semaphore is constructed without a maxCount so Release(delta)
        // will not throw when multiple grains raise the limit concurrently.
        lock (s_catchUpSemaphoreSync)
        {
            var current = s_catchUpMaxConcurrentBatches;
            if (target <= current)
            {
                return;
            }

            var delta = target - current;
            s_catchUpMaxConcurrentBatches = target;
            CatchUpBatchSemaphore.Release(delta);
        }
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
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
