using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Streams;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.ServiceId;
using System.Text;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Simplified pure infrastructure grain with minimal business logic
///     Demonstrates separation of concerns
/// </summary>
public class MultiProjectionGrain : Grain, IMultiProjectionGrain, ILifecycleParticipant<IGrainLifecycle>
{
    private readonly IProjectionActorHostFactory _actorHostFactory;
    private readonly IEventStore _eventStore;
    private readonly IPersistentState<MultiProjectionGrainState> _state;
    private readonly IEventSubscriptionResolver _subscriptionResolver;
    private readonly IMultiProjectionStateStore? _multiProjectionStateStore;
    private readonly GeneralMultiProjectionActorOptions? _injectedActorOptions;
    private readonly ILogger<MultiProjectionGrain> _logger;
    private string? _grainKey;
    private string? _projectorName;
    private string _serviceId = DefaultServiceIdProvider.DefaultServiceId;

    // State restoration tracking
    private DateTime? _stateRestoredAt;
    private StateRestoreSource _stateRestoreSource = StateRestoreSource.None;
    private bool _activationHealthy = true;  // Default to healthy for backward compatibility
    private string? _activationFailureReason;

    // Orleans infrastructure
    private IAsyncStream<SerializableEvent>? _orleansStream;
    private StreamSubscriptionHandle<SerializableEvent>? _orleansStreamHandle;
    private IDisposable? _persistTimer;
    private IDisposable? _fallbackTimer;

    // Projection host - engine-agnostic abstraction over the projection actor
    private IProjectionActorHost? _host;

    // Simple tracking
    private bool _isInitialized;
    private string? _lastError;
    private long _eventsProcessed;
    private HashSet<string> _processedEventIds = new(); // Track processed event IDs to prevent double counting
    private DateTime? _lastEventTime;

    // Event delivery statistics (debug/no-op selectable)
    private readonly Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics _eventStats;

    // Event batching
    private readonly List<SerializableEvent> _eventBuffer = new();
    private readonly HashSet<string> _unsafeEventIds = new(); // Track which buffered events are unsafe
    private DateTime _lastBufferFlush = DateTime.UtcNow;
    private readonly TimeSpan _batchTimeout = TimeSpan.FromMilliseconds(50); // Flush promptly but return quickly to stream
    private IDisposable? _batchTimer;
    private IDisposable? _immediateFlushTimer;
    private bool _subscriptionStarting;
    private DateTime _lastCatchUpUtc = DateTime.MinValue;
    private readonly TimeSpan _minCatchUpInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _overlapCooldown = TimeSpan.FromSeconds(10);

    // Catch-up state management
    private class CatchUpProgress
    {
        public SortableUniqueId? CurrentPosition { get; set; }
        public SortableUniqueId? TargetPosition { get; set; }
        public bool IsActive { get; set; }
        public int ConsecutiveEmptyBatches { get; set; }
        public DateTime LastAttempt { get; set; }
        public int BatchesProcessed { get; set; }
        public DateTime StartTime { get; set; }
    }

    private CatchUpProgress _catchUpProgress = new();
    private IDisposable? _catchUpTimer;
    private readonly Queue<SerializableEvent> _pendingStreamEvents = new();
    private const int DefaultCatchUpBatchSize = 500;
    private const int MaxConsecutiveEmptyBatches = 5; // More batches before considering complete
    private readonly TimeSpan _catchUpInterval = TimeSpan.FromSeconds(1); // Standard interval after performance fix
    private static readonly SemaphoreSlim CatchUpBatchSemaphore = new(1, 1);

    // Delegate these to configuration
    private readonly int _persistBatchSize = 1000; // Persist less frequently to avoid blocking deliveries
    private readonly TimeSpan _persistInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _fallbackCheckInterval = TimeSpan.FromSeconds(30);
    private int _maxPendingStreamEvents = 50000;
    private int _catchUpBatchSize = DefaultCatchUpBatchSize;

    public MultiProjectionGrain(
        [PersistentState("multiProjection", "OrleansStorage")] IPersistentState<MultiProjectionGrainState> state,
        IProjectionActorHostFactory actorHostFactory,
        IEventStore eventStore,
        IEventSubscriptionResolver? subscriptionResolver,
        IMultiProjectionStateStore? multiProjectionStateStore,
        Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics? eventStats,
        GeneralMultiProjectionActorOptions? actorOptions,
        ILogger<MultiProjectionGrain>? logger = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _actorHostFactory = actorHostFactory ?? throw new ArgumentNullException(nameof(actorHostFactory));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _subscriptionResolver = subscriptionResolver ?? new DefaultOrleansEventSubscriptionResolver();
        _multiProjectionStateStore = multiProjectionStateStore;
        _eventStats = eventStats ?? new Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics();
        _injectedActorOptions = actorOptions;
        _logger = logger ?? NullLogger<MultiProjectionGrain>.Instance;
    }

    private (string GrainKey, string ProjectorName, string ServiceId) GetIdentity()
    {
        if (!string.IsNullOrEmpty(_grainKey) && !string.IsNullOrEmpty(_projectorName))
        {
            return (_grainKey!, _projectorName!, _serviceId);
        }

        var grainKey = this.GetPrimaryKeyString();
        var parsed = ServiceIdGrainKey.Parse(grainKey);
        _grainKey = grainKey;
        _projectorName = parsed.RawKey;
        _serviceId = parsed.ServiceId;
        return (_grainKey, _projectorName, _serviceId);
    }

    private string GetProjectorName() => GetIdentity().ProjectorName;

    private string GetGrainKey() => GetIdentity().GrainKey;

    public async Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true, bool waitForCatchUp = false)
    {
        await EnsureInitializedAsync();

        if (_host == null)
        {
            return ResultBox.Error<MultiProjectionState>(
                new InvalidOperationException("Projection host not initialized"));
        }

        await StartSubscriptionAsync();

        // Start catch-up in background (fire-and-forget, does not block)
        _ = CatchUpFromEventStoreAsync();

        // If waitForCatchUp is true, wait with timeout
        if (waitForCatchUp && _catchUpProgress.IsActive)
        {
            await WaitForCatchUpWithTimeoutAsync(TimeSpan.FromSeconds(30));
        }

        var stateResult = await _host.GetStateAsync(canGetUnsafeState);

        // Enrich state with catch-up progress information
        return stateResult.Remap(state => EnrichStateWithCatchUpProgress(state));
    }

    /// <summary>
    ///     Enrich the projection state with catch-up progress information.
    /// </summary>
    private MultiProjectionState EnrichStateWithCatchUpProgress(MultiProjectionState state)
    {
        if (!_catchUpProgress.IsActive)
        {
            return state;
        }

        // Calculate approximate progress percentage based on batches processed
        double? progressPercent = null;
        if (_catchUpProgress.BatchesProcessed > 0 && _eventsProcessed > 0)
        {
            var elapsed = DateTime.UtcNow - _catchUpProgress.StartTime;
            if (elapsed.TotalSeconds > 1)
            {
                // Estimate based on events per second and typical event counts
                // This is a rough estimate since we don't know total event count upfront
                var eventsPerSecond = _eventsProcessed / elapsed.TotalSeconds;
                if (eventsPerSecond > 0)
                {
                    // Use batches processed as a proxy for progress
                    // Typical large projection might have 100+ batches
                    progressPercent = Math.Min(99.0, _catchUpProgress.BatchesProcessed * 1.5);
                }
            }
        }

        return state with
        {
            IsCatchUpInProgress = true,
            CatchUpCurrentPosition = _catchUpProgress.CurrentPosition?.Value,
            CatchUpTargetPosition = _catchUpProgress.TargetPosition?.Value,
            CatchUpProgressPercent = progressPercent
        };
    }

    /// <summary>
    ///     Wait for catch-up to complete with a timeout.
    /// </summary>
    private async Task WaitForCatchUpWithTimeoutAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var checkInterval = TimeSpan.FromMilliseconds(500);

        while (_catchUpProgress.IsActive && DateTime.UtcNow < deadline)
        {
            await Task.Delay(checkInterval);
        }
    }

    /// <summary>
    ///     Get event delivery statistics for debugging
    /// </summary>
    public Task<EventDeliveryStatistics> GetEventDeliveryStatisticsAsync()
    {
        var snap = _eventStats.Snapshot();
        var stats = new EventDeliveryStatistics
        {
            TotalUniqueEvents = snap.totalUnique,
            TotalDeliveries = snap.totalDeliveries,
            DuplicateDeliveries = snap.duplicateDeliveries,
            EventsWithMultipleDeliveries = snap.eventsWithMultipleDeliveries,
            MaxDeliveryCount = snap.maxDeliveryCount,
            AverageDeliveryCount = snap.averageDeliveryCount,
            StreamUniqueEvents = snap.streamUnique,
            StreamDeliveries = snap.streamDeliveries,
            CatchUpUniqueEvents = snap.catchUpUnique,
            CatchUpDeliveries = snap.catchUpDeliveries,
            Message = snap.message
        };

        return Task.FromResult(stats);
    }

    public Task<MultiProjectionCatchUpStatus> GetCatchUpStatusAsync()
    {
        var status = new MultiProjectionCatchUpStatus(
            _catchUpProgress.IsActive,
            _catchUpProgress.CurrentPosition?.Value,
            _catchUpProgress.TargetPosition?.Value,
            _catchUpProgress.BatchesProcessed,
            _catchUpProgress.ConsecutiveEmptyBatches,
            _catchUpProgress.StartTime,
            _catchUpProgress.LastAttempt,
            _pendingStreamEvents.Count);
        return Task.FromResult(status);
    }

    /// <summary>
    ///     Get health status for monitoring and diagnostics.
    ///     This method is safe to call even before initialization completes.
    /// </summary>
    public Task<MultiProjectionHealthStatus> GetHealthStatusAsync()
    {
        // Safe access - return defaults if not initialized
        var lastPersistTime = _state?.State?.LastPersistTime;
        var lastSortableUniqueId = _state?.State?.LastSortableUniqueId;
        var pendingCount = _pendingStreamEvents?.Count ?? 0;
        var isCatchUpActive = _catchUpProgress?.IsActive ?? false;

        return Task.FromResult(new MultiProjectionHealthStatus(
            IsInitialized: _isInitialized,
            HasProjectionActor: _host != null,
            EventsProcessed: _eventsProcessed,
            LastError: _lastError,
            IsCatchUpActive: isCatchUpActive,
            LastPersistTime: lastPersistTime == default ? null : lastPersistTime,
            LastSortableUniqueId: lastSortableUniqueId,
            PendingStreamEvents: pendingCount,
            StateRestoredAt: _stateRestoredAt,
            StateRestoreSource: _stateRestoreSource,
            IsHealthy: _activationHealthy
        ));
    }

    public async Task<ResultBox<string>> GetSnapshotJsonAsync(bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();

        if (_host == null)
        {
            return ResultBox.Error<string>(
                new InvalidOperationException("Projection host not initialized"));
        }

        var rb = await _host.GetSnapshotBytesAsync(canGetUnsafeState);
        if (!rb.IsSuccess)
            return ResultBox.Error<string>(rb.GetException());
        // v10 format: byte[] is plain UTF-8 JSON
        return ResultBox.FromValue(Encoding.UTF8.GetString(rb.GetValue()));
    }

    public async Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true)
    {
        await EnsureInitializedAsync();

        if (_host == null)
        {
            throw new InvalidOperationException("Projection host not initialized");
        }

        // Track event deliveries as well for events coming from the EventStore catch-up
        // so that delivery statistics include both stream and catch-up paths.
        _eventStats.RecordCatchUpBatch(events);

        // Filter out already processed events to prevent double counting
        var newEvents = events.Where(e => !_processedEventIds.Contains(e.Id.ToString())).ToList();

        if (newEvents.Count > 0)
        {
            // Delegate to host (Event→SerializableEvent conversion happens internally)
            await _host.AddEventsFromCatchUpAsync(newEvents, finishedCatchUp);
            _eventsProcessed += newEvents.Count;

            // Mark events as processed
            foreach (var ev in newEvents)
            {
                _processedEventIds.Add(ev.Id.ToString());
            }

            if (newEvents.Count > 0)
            {
                _lastEventTime = DateTime.UtcNow;
            }
        }
    }

    public async Task<MultiProjectionGrainStatus> GetStatusAsync()
    {
        var currentPosition = _state.State?.LastPosition;
        var isCaughtUp = _orleansStreamHandle != null;

        long stateSize = 0;
        long safeStateSize = 0;
        long unsafeStateSize = 0;
        if (_host != null)
        {
            try
            {
                unsafeStateSize = _host.EstimateStateSizeBytes(includeUnsafeDetails: true);
                safeStateSize = _host.EstimateStateSizeBytes(includeUnsafeDetails: false);
                stateSize = safeStateSize; // Backward-compatible: report safe payload size in StateSize
                var projectorName = GetProjectorName();
                _logger.LogDebug(
                    "[{ProjectorName}] State size - Safe: {SafeBytes:N0} bytes, Unsafe: {UnsafeBytes:N0} bytes, Events: {EventsProcessed:N0}",
                    projectorName,
                    safeStateSize,
                    unsafeStateSize,
                    _eventsProcessed);
            }
            catch
            {
                // Ignore errors when estimating size during status fetch
            }
        }

        return new MultiProjectionGrainStatus(
            GetProjectorName(),
            _orleansStreamHandle != null,
            isCaughtUp,
            currentPosition,
            _eventsProcessed,
            _lastEventTime,
            DateTime.UtcNow,
            stateSize,
            safeStateSize,
            unsafeStateSize,
            !string.IsNullOrEmpty(_lastError),
            _lastError);
    }

    // Threshold for forcing GC before serialization (10MB payload)
    private const long LargePayloadThresholdBytes = 10_000_000;

    public async Task<ResultBox<bool>> PersistStateAsync()
    {
        try
        {
            var startUtc = DateTime.UtcNow;
            var projectorName = GetProjectorName();

            if (_host == null)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("Projection host not initialized"));
            }
            _logger.LogDebug(
                "[{ProjectorName}] Starting persistence at {StartUtc:yyyy-MM-dd HH:mm:ss.fff} UTC",
                projectorName,
                startUtc);

            // Phase1: force promotion of buffered events before snapshot
            try
            {
                _host.ForcePromoteBufferedEvents();
            }
            catch { }

            // Get snapshot as opaque bytes from the host
            var snapshotBytesResult = await _host.GetSnapshotBytesAsync(canGetUnsafeState: false);
            if (!snapshotBytesResult.IsSuccess)
            {
                _lastError = snapshotBytesResult.GetException().Message;
                _logger.LogWarning("[{ProjectorName}] {LastError}", projectorName, _lastError);
                return ResultBox.FromValue(false);
            }

            var envelopeBytes = snapshotBytesResult.GetValue();
            var envelopeSize = (long)envelopeBytes.Length;

            // Get metadata via host
            int? safeVersion = null;
            int? unsafeVersion = null;
            long originalSizeBytes = envelopeSize;
            long compressedSizeBytes = envelopeSize;
            try
            {
                var metadataResult = await _host.GetStateMetadataAsync(includeUnsafe: true);
                if (metadataResult.IsSuccess)
                {
                    var metadata = metadataResult.GetValue();
                    safeVersion = metadata.SafeVersion;
                    unsafeVersion = metadata.UnsafeVersion;
                    var safeLastId = metadata.SafeLastSortableUniqueId ?? string.Empty;
                    if (safeLastId.Length >= 20) safeLastId = safeLastId.Substring(0, 20);
                    var unsafeLastId = metadata.UnsafeLastSortableUniqueId ?? string.Empty;
                    if (unsafeLastId.Length >= 20) unsafeLastId = unsafeLastId.Substring(0, 20);
                    _logger.LogDebug(
                        "[{ProjectorName}] Snapshot state - Safe: {SafeVersion} events @ {SafeLastId}, Unsafe: {UnsafeVersion} events @ {UnsafeLastId}",
                        projectorName,
                        metadata.SafeVersion,
                        safeLastId.Length > 0 ? safeLastId : "empty",
                        metadata.UnsafeVersion,
                        unsafeLastId.Length > 0 ? unsafeLastId : "empty");
                }
            }
            catch { }

            var projectorVersion = _host.GetProjectorVersion();

            // Get safe position from the host
            var safePosition = await _host.GetSafeLastSortableUniqueIdAsync();

            string? safeThresholdValue = null;
            DateTime? safeThresholdTime = null;
            try
            {
                safeThresholdValue = _host.PeekCurrentSafeWindowThreshold();
                var safeThresholdId = new SortableUniqueId(safeThresholdValue);
                safeThresholdTime = safeThresholdId.GetDateTime();
            }
            catch { }

            _logger.LogDebug(
                "[{ProjectorName}] v10: Writing snapshot: {EnvelopeSize:N0} bytes, {EventsProcessed:N0} events, checkpoint: {Checkpoint}",
                projectorName,
                envelopeSize,
                _eventsProcessed,
                (safePosition?.Length >= 20 ? safePosition.Substring(0, 20) : safePosition) ?? "empty");
            _logger.LogInformation(
                MultiProjectionLogEvents.PersistDetails,
                "Persist: {ProjectorName}, Events={EventsProcessed}, SafeVer={SafeVersion}, UnsafeVer={UnsafeVersion}, EnvelopeSize={EnvelopeSize}, SafeThreshold={SafeThreshold}",
                projectorName,
                _eventsProcessed,
                safeVersion,
                unsafeVersion,
                envelopeSize,
                safeThresholdTime);

            // Integrity guard: Block persist if safeVersion regressed (indicates data corruption)
            var lastGoodSafeVersion = _state.State?.LastGoodSafeVersion ?? 0;
            if (safeVersion.HasValue && lastGoodSafeVersion > 0 && safeVersion.Value < lastGoodSafeVersion)
            {
                _lastError = $"Integrity guard blocked persist: safeVersion {safeVersion.Value} < LastGoodSafeVersion {lastGoodSafeVersion}";
                _logger.LogError(
                    MultiProjectionLogEvents.IntegrityGuardBlockedPersist,
                    "BLOCKED persist: {ProjectorName} - safeVersion regression detected. Current={CurrentSafeVersion}, LastGood={LastGoodSafeVersion}. State will NOT be saved.",
                    projectorName,
                    safeVersion.Value,
                    lastGoodSafeVersion);
                _stateRestoreSource = StateRestoreSource.Failed;
                return ResultBox.FromValue(false);
            }

            var externalStoreSaved = _multiProjectionStateStore == null;
            var allowExternalStoreSave = true;

            if (_multiProjectionStateStore != null)
            {
                var latestResult = await _multiProjectionStateStore
                    .GetLatestForVersionAsync(projectorName, projectorVersion);
                if (!latestResult.IsSuccess)
                {
                    allowExternalStoreSave = false;
                    _lastError = $"External store read failed: {latestResult.GetException().Message}";
                    _logger.LogWarning(
                        "Skip external store save: failed to read latest state for {ProjectorName} v{ProjectorVersion}.",
                        projectorName,
                        projectorVersion);
                }
                else
                {
                    var latestOptional = latestResult.GetValue();
                    if (latestOptional.HasValue &&
                        latestOptional.Value is { } latestRecord &&
                        latestRecord.EventsProcessed > _eventsProcessed)
                    {
                        allowExternalStoreSave = false;
                        _lastError = $"External store has newer state ({latestRecord.EventsProcessed}) than local ({_eventsProcessed})";
                        _logger.LogWarning(
                            "Skip external store save: latest EventsProcessed {LatestEvents} > local {LocalEvents} for {ProjectorName} v{ProjectorVersion}.",
                            latestRecord.EventsProcessed,
                            _eventsProcessed,
                            projectorName,
                            projectorVersion);
                    }
                }
            }

            // v10: Save to external store (Postgres/Cosmos) if available
            if (_multiProjectionStateStore != null && allowExternalStoreSave)
            {
                var record = new MultiProjectionStateRecord(
                    ProjectorName: projectorName,
                    ProjectorVersion: projectorVersion,
                    PayloadType: typeof(SerializableMultiProjectionStateEnvelope).FullName!,
                    LastSortableUniqueId: safePosition ?? string.Empty,
                    EventsProcessed: _eventsProcessed,
                    StateData: envelopeBytes,  // v10: No outer compression
                    IsOffloaded: false,
                    OffloadKey: null,
                    OffloadProvider: null,
                    OriginalSizeBytes: originalSizeBytes,
                    CompressedSizeBytes: compressedSizeBytes,
                    SafeWindowThreshold: _host.PeekCurrentSafeWindowThreshold(),
                    CreatedAt: _state.State!.LastPersistTime == default
                        ? DateTime.UtcNow
                        : _state.State.LastPersistTime,
                    UpdatedAt: DateTime.UtcNow,
                    BuildSource: "GRAIN",
                    BuildHost: Environment.MachineName);

                var saveResult = await _multiProjectionStateStore.UpsertAsync(record);
                if (!saveResult.IsSuccess)
                {
                    _lastError = $"External store save failed: {saveResult.GetException().Message}";
                    _logger.LogWarning("[{ProjectorName}] {LastError}", projectorName, _lastError);
                    // Continue to save Orleans state as fallback info
                }
                else
                {
                    externalStoreSaved = true;
                    _logger.LogDebug("[{ProjectorName}] External store save succeeded", projectorName);
                }
            }
            else if (_multiProjectionStateStore != null && !allowExternalStoreSave)
            {
                _logger.LogDebug("[{ProjectorName}] External store save skipped (store ahead or read failed)", projectorName);
            }

            // v9: Update Orleans state with key info only (auxiliary/monitoring)
            _state.State!.ProjectorName = projectorName;
            _state.State.ProjectorVersion = projectorVersion;
            _state.State.LastSortableUniqueId = safePosition;
            _state.State.EventsProcessed = _eventsProcessed;
            _state.State.LastPersistTime = DateTime.UtcNow;

            // Update LastGood fields only when the external store save succeeded.
            if (externalStoreSaved)
            {
                if (safeVersion.HasValue && safeVersion.Value > 0)
                {
                    _state.State.LastGoodSafeVersion = safeVersion.Value;
                }
                if (envelopeSize > 0)
                {
                    _state.State.LastGoodPayloadBytes = envelopeSize;
                }
                if (originalSizeBytes > 0)
                {
                    _state.State.LastGoodOriginalSizeBytes = originalSizeBytes;
                }
                _state.State.LastGoodEventsProcessed = _eventsProcessed;
            }

            // Clear legacy fields
            _state.State.SerializedState = null;
            _state.State.StateSize = 0;
            _state.State.SafeLastPosition = null;
            _state.State.LastPosition = null;

            // Retry Orleans state write on ETag conflicts (optimistic concurrency)
            const int maxRetries = 3;
            for (var retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    await _state.WriteStateAsync();
                    break; // Success
                }
                catch (global::Orleans.Storage.InconsistentStateException) when (retry < maxRetries - 1)
                {
                    _logger.LogWarning(
                        "[{ProjectorName}] ETag conflict on Orleans state write (attempt {Attempt}/{MaxAttempts}), re-reading state...",
                        projectorName,
                        retry + 1,
                        maxRetries);
                    // Re-read state to get fresh ETag, then re-apply our changes
                    await _state.ReadStateAsync();
                    _state.State.ProjectorName = projectorName;
                    _state.State.ProjectorVersion = projectorVersion;
                    _state.State.LastSortableUniqueId = safePosition;
                    _state.State.EventsProcessed = _eventsProcessed;
                    _state.State.LastPersistTime = DateTime.UtcNow;
                    // Re-apply LastGood fields when the external store save succeeded.
                    if (externalStoreSaved)
                    {
                        if (safeVersion.HasValue && safeVersion.Value > 0)
                        {
                            _state.State.LastGoodSafeVersion = safeVersion.Value;
                        }
                        if (envelopeSize > 0)
                        {
                            _state.State.LastGoodPayloadBytes = envelopeSize;
                        }
                        if (originalSizeBytes > 0)
                        {
                            _state.State.LastGoodOriginalSizeBytes = originalSizeBytes;
                        }
                        _state.State.LastGoodEventsProcessed = _eventsProcessed;
                    }
                    _state.State.SerializedState = null;
                    _state.State.StateSize = 0;
                    _state.State.SafeLastPosition = null;
                    _state.State.LastPosition = null;
                    await Task.Delay(50 * (retry + 1)); // Brief backoff
                }
            }
            _lastError = null;
            var finishUtc = DateTime.UtcNow;
            _logger.LogDebug(
                "[{ProjectorName}] Persistence completed in {ElapsedMs:F0}ms - {EnvelopeSize:N0} bytes, {EventsProcessed:N0} events saved",
                projectorName,
                (finishUtc - startUtc).TotalMilliseconds,
                envelopeSize,
                _eventsProcessed);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _lastError = $"Persistence failed: {ex.Message}";
            _logger.LogError(ex, "[{ProjectorName}] Persistence failed", GetProjectorName());
            return ResultBox.Error<bool>(ex);
        }
    }

    // Debug: force promotion of ALL buffered events regardless of window
    public Task ForcePromoteAllAsync()
    {
        if (_host != null)
        {
            try
            {
                _host.ForcePromoteAllBufferedEvents();
            }
            catch { }
        }
        return Task.CompletedTask;
    }

    public async Task StopSubscriptionAsync()
    {
        if (_orleansStreamHandle != null)
        {
            await _orleansStreamHandle.UnsubscribeAsync();
            _orleansStreamHandle = null;
        }
    }

    public async Task StartSubscriptionAsync()
    {
        await EnsureInitializedAsync();

        // Defensive: ensure stream is prepared even if lifecycle hook hasn't run yet
        if (_orleansStream == null)
        {
            var grainKey = GetGrainKey();
            var streamInfo = _subscriptionResolver.Resolve(grainKey);
            if (streamInfo is OrleansSekibanStream orleansStream)
            {
                var streamProvider = this.GetStreamProvider(orleansStream.ProviderName);
                _orleansStream = streamProvider.GetStream<SerializableEvent>(
                    StreamId.Create(orleansStream.StreamNamespace, orleansStream.StreamId));
            }
        }

        // Subscribe to Orleans stream if not already subscribed
        if (_orleansStreamHandle == null && _orleansStream != null && !_subscriptionStarting)
        {
            try
            {
                _subscriptionStarting = true;
                var projectorName = GetProjectorName();
                _logger.LogDebug("[SimplifiedPureGrain-{ProjectorName}] Starting subscription to Orleans stream", projectorName);

                var observer = new StreamBatchObserver(this);

                // Check for existing persistent subscriptions and resume/deduplicate
                var existing = await _orleansStream.GetAllSubscriptionHandles();
                if (existing != null && existing.Count > 0)
                {
                    // Resume the oldest handle
                    var primary = existing[0];
                    _orleansStreamHandle = await primary.ResumeAsync(observer);
                    _logger.LogDebug(
                        "[SimplifiedPureGrain-{ProjectorName}] Resumed existing stream subscription ({HandleCount} handles found)",
                        projectorName,
                        existing.Count);

                    // Unsubscribe duplicates
                    for (int i = 1; i < existing.Count; i++)
                    {
                        try
                        {
                            await existing[i].UnsubscribeAsync();
                            _logger.LogDebug(
                                "[SimplifiedPureGrain-{ProjectorName}] Unsubscribed duplicate stream subscription handle #{HandleIndex}",
                                projectorName,
                                i);
                        }
                        catch (Exception exDup)
                        {
                            _logger.LogWarning(
                                exDup,
                                "[SimplifiedPureGrain-{ProjectorName}] Failed to unsubscribe duplicate handle #{HandleIndex}",
                                projectorName,
                                i);
                        }
                    }
                }
                else
                {
                    _orleansStreamHandle = await _orleansStream.SubscribeAsync(observer, null);
                    _logger.LogDebug("[SimplifiedPureGrain-{ProjectorName}] Successfully subscribed to Orleans stream (new)", projectorName);
                }
            }
            catch (Exception ex)
            {
                var projectorName = GetProjectorName();
                _logger.LogError(ex, "[SimplifiedPureGrain-{ProjectorName}] Failed to subscribe to Orleans stream", projectorName);
                _lastError = $"Stream subscription failed: {ex.Message}";
                throw;
            }
            finally
            {
                _subscriptionStarting = false;
            }
        }
        else if (_orleansStreamHandle != null)
        {
            var projectorName = GetProjectorName();
            _logger.LogDebug("[SimplifiedPureGrain-{ProjectorName}] Stream subscription already active", projectorName);
        }
        // Do not auto-catch-up here; catch-up will be triggered by state/query access
    }

    public async Task<SerializableQueryResult> ExecuteQueryAsync(SerializableQueryParameter queryParameter)
    {
        // Check health if FailOnUnhealthyActivation is enabled
        if (_injectedActorOptions?.FailOnUnhealthyActivation == true && !_activationHealthy)
        {
            _logger.LogWarning(
                MultiProjectionLogEvents.QueryRejected,
                "Query rejected due to unhealthy activation: {ProjectorName}",
                GetProjectorName());
            throw new InvalidOperationException($"Projection not healthy: {_activationFailureReason}");
        }

        await EnsureInitializedAsync();

        if (_host == null)
        {
            return new SerializableQueryResult();
        }

        try
        {
            await StartSubscriptionAsync();

            // Get safe/unsafe metadata for query context
            int? safeVersion = null;
            string? safeThreshold = null;
            DateTime? safeThresholdTime = null;
            int? unsafeVersion = null;

            var safeStateResult = await _host.GetStateAsync(canGetUnsafeState: false);
            if (safeStateResult.IsSuccess)
            {
                safeVersion = safeStateResult.GetValue().Version;
            }

            safeThreshold = _host.PeekCurrentSafeWindowThreshold();
            try
            {
                var safeThresholdId = new SortableUniqueId(safeThreshold);
                safeThresholdTime = safeThresholdId.GetDateTime();
            }
            catch { }

            var unsafeStateResult = await _host.GetStateAsync(canGetUnsafeState: true);
            if (unsafeStateResult.IsSuccess)
            {
                unsafeVersion = unsafeStateResult.GetValue().Version;
            }

            if (_orleansStreamHandle == null)
            {
                await CatchUpFromEventStoreAsync();
            }

            var result = await _host.ExecuteQueryAsync(
                queryParameter,
                safeVersion,
                safeThreshold,
                safeThresholdTime,
                unsafeVersion);

            if (!result.IsSuccess)
            {
                throw result.GetException();
            }

            return result.GetValue();
        }
        catch (Exception ex)
        {
            _lastError = $"Query failed: {ex.Message}";
            throw;
        }
    }

    public async Task<SerializableListQueryResult> ExecuteListQueryAsync(SerializableQueryParameter queryParameter)
    {
        // Check health if FailOnUnhealthyActivation is enabled
        if (_injectedActorOptions?.FailOnUnhealthyActivation == true && !_activationHealthy)
        {
            _logger.LogWarning(
                MultiProjectionLogEvents.QueryRejected,
                "List query rejected due to unhealthy activation: {ProjectorName}",
                GetProjectorName());
            throw new InvalidOperationException($"Projection not healthy: {_activationFailureReason}");
        }

        await EnsureInitializedAsync();

        if (_host == null)
        {
            return new SerializableListQueryResult();
        }

        try
        {
            await StartSubscriptionAsync();

            // Get safe/unsafe metadata for query context
            int? safeVersion = null;
            string? safeThreshold = null;
            DateTime? safeThresholdTime = null;
            int? unsafeVersion = null;

            var safeStateResult = await _host.GetStateAsync(canGetUnsafeState: false);
            if (safeStateResult.IsSuccess)
            {
                safeVersion = safeStateResult.GetValue().Version;
            }

            safeThreshold = _host.PeekCurrentSafeWindowThreshold();
            try
            {
                var safeThresholdId = new SortableUniqueId(safeThreshold);
                safeThresholdTime = safeThresholdId.GetDateTime();
            }
            catch { }

            var unsafeStateResult = await _host.GetStateAsync(canGetUnsafeState: true);
            if (unsafeStateResult.IsSuccess)
            {
                unsafeVersion = unsafeStateResult.GetValue().Version;
            }

            if (_orleansStreamHandle == null)
            {
                await CatchUpFromEventStoreAsync();
            }

            var result = await _host.ExecuteListQueryAsync(
                queryParameter,
                safeVersion,
                safeThreshold,
                safeThresholdTime,
                unsafeVersion);

            if (!result.IsSuccess)
            {
                throw result.GetException();
            }

            return result.GetValue();
        }
        catch (Exception ex)
        {
            _lastError = $"List query failed: {ex.Message}";
            throw;
        }
    }

    public async Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        await EnsureInitializedAsync();

        if (_host == null) return false;

        return await _host.IsSortableUniqueIdReceivedAsync(sortableUniqueId);
    }

    public async Task RefreshAsync()
    {
        _logger.LogDebug("[{ProjectorName}] Refreshing: Re-reading events from event store", GetProjectorName());
        await CatchUpFromEventStoreAsync();
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var projectorName = GetProjectorName();
        _logger.LogInformation(
            MultiProjectionLogEvents.ActivationStarted,
            "Grain activation started: {ProjectorName}",
            projectorName);

        // Validate Orleans state - if corrupted or incompatible, reset it
        try
        {
            if (_state.State == null)
            {
                _logger.LogDebug("Orleans state is null, will initialize fresh: {ProjectorName}", projectorName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans state access failed, clearing: {ProjectorName}", projectorName);
            try
            {
                await _state.ClearStateAsync();
            }
            catch
            {
                // Ignore clear errors - we'll proceed with fresh state
            }
        }

        // Create projection host via factory
        bool forceFullCatchUp = false;
        if (_host == null)
        {
            _logger.LogDebug("Creating new projection host: {ProjectorName}", projectorName);
            // Merge injected options - snapshot offload is handled by IMultiProjectionStateStore
            var baseOptions = _injectedActorOptions ?? new GeneralMultiProjectionActorOptions();
            var mergedOptions = new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = baseOptions.SafeWindowMs,
                MaxSnapshotSerializedSizeBytes = baseOptions.MaxSnapshotSerializedSizeBytes,
                MaxPendingStreamEvents = baseOptions.MaxPendingStreamEvents,
                CatchUpBatchSize = baseOptions.CatchUpBatchSize,
                EnableDynamicSafeWindow = baseOptions.EnableDynamicSafeWindow,
                MaxExtraSafeWindowMs = baseOptions.MaxExtraSafeWindowMs,
                LagEmaAlpha = baseOptions.LagEmaAlpha,
                LagDecayPerSecond = baseOptions.LagDecayPerSecond,
                FailOnUnhealthyActivation = baseOptions.FailOnUnhealthyActivation
            };
            _maxPendingStreamEvents = mergedOptions.MaxPendingStreamEvents;
            _catchUpBatchSize = Math.Max(1, mergedOptions.CatchUpBatchSize);

            _host = _actorHostFactory.Create(
                projectorName,
                mergedOptions,
                _logger);

            var projectorVersion = _host.GetProjectorVersion();
            bool restoredFromExternalStore = false;

            // Restore from external store (Postgres/Cosmos)
            if (_multiProjectionStateStore != null)
            {
                try
                {
                    _logger.LogInformation(
                        "Restoring from external store (version match): {ProjectorName} v{ProjectorVersion}",
                        projectorName,
                        projectorVersion);
                    var stateStoreResult = await _multiProjectionStateStore.GetLatestForVersionAsync(
                        projectorName,
                        projectorVersion,
                        cancellationToken);

                    if (!stateStoreResult.IsSuccess)
                    {
                        // Explicit error from state store (e.g., blob read failure)
                        var errorMsg = stateStoreResult.GetException().Message;
                        _logger.LogError(
                            MultiProjectionLogEvents.StateRestoreFailed,
                            stateStoreResult.GetException(),
                            "External store query failed: {ProjectorName}, Error: {Error}",
                            projectorName, errorMsg);
                        _stateRestoreSource = StateRestoreSource.Failed;
                        _activationFailureReason = errorMsg;
                        forceFullCatchUp = true;
                    }
                    else if (stateStoreResult.GetValue().HasValue)
                    {
                        var record = stateStoreResult.GetValue().GetValue();
                        byte[]? snapshotData = record.StateData;

                        if (snapshotData == null)
                        {
                            var errorMsg = record.IsOffloaded
                                ? $"Blob read returned null for key: {record.OffloadKey}"
                                : "StateData is null but not marked as offloaded";
                            _logger.LogError(
                                MultiProjectionLogEvents.BlobReadFailed,
                                "State data is null: {ProjectorName}, IsOffloaded: {IsOffloaded}, OffloadKey: {OffloadKey}",
                                projectorName, record.IsOffloaded, record.OffloadKey);
                            _stateRestoreSource = StateRestoreSource.Failed;
                            _activationFailureReason = errorMsg;
                            forceFullCatchUp = true;
                        }
                        else
                        {
                            // Delegate snapshot restoration to the host (handles format detection, deserialization, validation)
                            var restoreResult = await _host.RestoreSnapshotAsync(snapshotData);

                            if (!restoreResult.IsSuccess)
                            {
                                _logger.LogError(
                                    MultiProjectionLogEvents.StateRestoreFailed,
                                    restoreResult.GetException(),
                                    "Snapshot restore failed: {ProjectorName}",
                                    projectorName);
                                _stateRestoreSource = StateRestoreSource.Failed;
                                _activationFailureReason = restoreResult.GetException().Message;
                                forceFullCatchUp = true;
                            }
                            else
                            {
                                _eventsProcessed = record.EventsProcessed;
                                _processedEventIds.Clear();

                                int? postSafeVersion = null;
                                int? postUnsafeVersion = null;
                                try
                                {
                                    var metadataResult = await _host.GetStateMetadataAsync(includeUnsafe: true);
                                    if (metadataResult.IsSuccess)
                                    {
                                        var metadata = metadataResult.GetValue();
                                        postSafeVersion = metadata.SafeVersion;
                                        postUnsafeVersion = metadata.UnsafeVersion;
                                    }
                                }
                                catch { }

                                _logger.LogInformation(
                                    MultiProjectionLogEvents.RestoreDetails,
                                    "Restore: {ProjectorName}, RecordEvents={RecordEvents}, StateDataLen={StateDataLen}, Original={OriginalSize}, Compressed={CompressedSize}, PostSafeVer={PostSafeVersion}, PostUnsafeVer={PostUnsafeVersion}",
                                    projectorName,
                                    record.EventsProcessed,
                                    record.StateData?.Length ?? 0,
                                    record.OriginalSizeBytes,
                                    record.CompressedSizeBytes,
                                    postSafeVersion,
                                    postUnsafeVersion);

                                if (record.EventsProcessed > 1000 && postSafeVersion == 0)
                                {
                                    _logger.LogWarning(
                                        MultiProjectionLogEvents.SafeVersionZero,
                                        "SUSPICIOUS: {ProjectorName} - {EventsProcessed} events but safeVersion=0 after restore",
                                        projectorName,
                                        record.EventsProcessed);
                                }

                                _logger.LogInformation(
                                    MultiProjectionLogEvents.StateRestoreSuccess,
                                    "State restored: {ProjectorName}, Position: {Position}, Events: {Events}",
                                    projectorName, record.LastSortableUniqueId, record.EventsProcessed);
                                restoredFromExternalStore = true;
                                _stateRestoredAt = DateTime.UtcNow;
                                _stateRestoreSource = StateRestoreSource.ExternalStore;
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            MultiProjectionLogEvents.StateNotFound,
                            "No state found in external store: {ProjectorName} v{ProjectorVersion}",
                            projectorName,
                            projectorVersion);

                        // Reset integrity guard fields when external snapshot is missing.
                        // Without this, LastGoodSafeVersion from a previous successful run
                        // permanently blocks persist because catch-up starts at safeVersion=0.
                        if (_state.State != null && _state.State.LastGoodSafeVersion > 0)
                        {
                            _logger.LogWarning(
                                "Resetting integrity guard: LastGoodSafeVersion was {LastGood} but external snapshot is missing. "
                                + "This allows catch-up to rebuild and persist a new snapshot. {ProjectorName}",
                                _state.State.LastGoodSafeVersion,
                                projectorName);
                            _state.State.LastGoodSafeVersion = 0;
                            _state.State.LastGoodPayloadBytes = 0;
                            _state.State.LastGoodOriginalSizeBytes = 0;
                            _state.State.LastGoodEventsProcessed = 0;
                            await _state.WriteStateAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        MultiProjectionLogEvents.StateRestoreFailed,
                        ex,
                        "State restoration exception: {ProjectorName}",
                        projectorName);
                    _stateRestoreSource = StateRestoreSource.Failed;
                    _activationFailureReason = ex.Message;
                    forceFullCatchUp = true;
                }
            }
            else if (_multiProjectionStateStore == null)
            {
                _logger.LogDebug(
                    MultiProjectionLogEvents.NoExternalStore,
                    "No external state store configured: {ProjectorName}",
                    projectorName);
            }

            if (!restoredFromExternalStore)
            {
                _logger.LogInformation("No persisted state, will perform full catch-up: {ProjectorName}", projectorName);
                forceFullCatchUp = true;
            }
        }

        await base.OnActivateAsync(cancellationToken);

        // After activation, start catch-up in background (fire-and-forget).
        // This prevents Orleans activation timeout when catch-up takes longer than 30 seconds.
        // Queries will return partial/stale data with IsCatchUpInProgress=true until catch-up completes.
        _ = CatchUpFromEventStoreAsync(forceFullCatchUp);

        // Auto-start subscription so stream-only projections resume after crashes/restarts.
        try
        {
            await StartSubscriptionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to auto-start stream subscription on activation: {ProjectorName}",
                projectorName);
        }

        // Mark state restore source based on whether we need full catch-up
        if (_stateRestoreSource == StateRestoreSource.Failed && _eventsProcessed == 0)
        {
            // External store restore failed, catch-up will rebuild state
            _activationHealthy = false;
            _logger.LogWarning(
                MultiProjectionLogEvents.UnhealthyActivation,
                "Grain activated without persisted state - catch-up in progress: {ProjectorName}, Reason: {Reason}",
                projectorName, _activationFailureReason);
        }
        else if (forceFullCatchUp && _stateRestoreSource != StateRestoreSource.Failed)
        {
            _stateRestoreSource = StateRestoreSource.FullCatchUp;
            _stateRestoredAt = DateTime.UtcNow;
        }

        _logger.LogInformation(
            MultiProjectionLogEvents.ActivationCompleted,
            "Grain activation completed (catch-up running in background): {ProjectorName}",
            projectorName);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        var projectorName = GetProjectorName();
        _logger.LogInformation(
            MultiProjectionLogEvents.DeactivationStarted,
            "Grain deactivation started: {ProjectorName}, Reason: {Reason}",
            projectorName, reason);

        // Stop catch-up if active
        if (_catchUpProgress.IsActive)
        {
            _catchUpProgress.IsActive = false;
            _catchUpTimer?.Dispose();
            _catchUpTimer = null;
        }

        // Persist state before deactivation
        try
        {
            var persistResult = await PersistStateAsync();
            if (!persistResult.IsSuccess)
            {
                _logger.LogError(
                    MultiProjectionLogEvents.DeactivationPersistFailed,
                    persistResult.GetException(),
                    "PersistStateAsync failed during deactivation: {ProjectorName}",
                    projectorName);
            }
            else if (!persistResult.GetValue())
            {
                _logger.LogWarning(
                    MultiProjectionLogEvents.DeactivationPersistFailed,
                    "PersistStateAsync returned false during deactivation: {ProjectorName}",
                    projectorName);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                MultiProjectionLogEvents.DeactivationPersistCancelled,
                "State persistence cancelled during shutdown: {ProjectorName}",
                projectorName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                MultiProjectionLogEvents.DeactivationPersistFailed,
                ex,
                "Failed to persist state during deactivation: {ProjectorName}",
                projectorName);
        }

        // Clean up Orleans resources
        try
        {
            if (_orleansStreamHandle != null)
            {
                await _orleansStreamHandle.UnsubscribeAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Stream unsubscription cancelled: {ProjectorName}", projectorName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unsubscribe from stream: {ProjectorName}", projectorName);
        }

        // Flush any remaining events
        try
        {
            await FlushEventBufferAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush event buffer: {ProjectorName}", projectorName);
        }

        _persistTimer?.Dispose();
        _fallbackTimer?.Dispose();
        _batchTimer?.Dispose();
        _catchUpTimer?.Dispose();

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private async Task ResetPersistedStateForFullRebuildAsync(string? currentVersion)
    {
        try
        {
            if (_state.State != null)
            {
                _state.State.ProjectorName = GetProjectorName();
                _state.State.SerializedState = null;
                _state.State.LastPosition = null;
                _state.State.SafeLastPosition = null;
                _state.State.EventsProcessed = 0;
                _state.State.StateSize = 0;
                _state.State.LastPersistTime = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(currentVersion))
                {
                    _state.State.ProjectorVersion = currentVersion;
                }
                await _state.WriteStateAsync();
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to clear persisted state: {ex.Message}";
        }

        _eventsProcessed = 0;
        _processedEventIds.Clear();
        _unsafeEventIds.Clear();
        _eventBuffer.Clear();
        _pendingStreamEvents.Clear();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        _isInitialized = true;

        // Set up periodic persistence timer
        _persistTimer = this.RegisterGrainTimer(
            async () => await PersistStateAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = _persistInterval,
                Period = _persistInterval,
                Interleave = true
            });

        // Set up fallback check timer
        _fallbackTimer = this.RegisterGrainTimer(
            async () => await FallbackEventCheckAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = _fallbackCheckInterval,
                Period = TimeSpan.FromMinutes(1),
                Interleave = true
            });

        // Set up batch flush timer
        _batchTimer = this.RegisterGrainTimer(
            async () => await FlushEventBufferAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = _batchTimeout,
                Period = _batchTimeout,
                Interleave = true
            });
    }

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public async Task<bool> OverwritePersistedStateVersionAsync(string newVersion)
    {
        try
        {
            var projectorName = GetProjectorName();
            var currentVersion = _state.State?.ProjectorVersion;
            bool updated = false;

            // Update external store (Postgres/Cosmos) via host
            if (_multiProjectionStateStore != null && !string.IsNullOrEmpty(currentVersion) && _host != null)
            {
                var stateResult = await _multiProjectionStateStore.GetLatestForVersionAsync(
                    projectorName, currentVersion);

                if (stateResult.IsSuccess && stateResult.GetValue().HasValue)
                {
                    var record = stateResult.GetValue().GetValue();

                    // Only envelope format supported
                    if (record.PayloadType != typeof(SerializableMultiProjectionStateEnvelope).FullName)
                    {
                        throw new InvalidOperationException(
                            $"Legacy format not supported. PayloadType: {record.PayloadType}. Please delete old snapshots and rebuild.");
                    }

                    // Delegate version rewriting to the host (handles format detection, deserialization, version patching)
                    var modifiedBytes = _host.RewriteSnapshotVersion(record.StateData!, newVersion);

                    var modifiedRecord = new MultiProjectionStateRecord(
                        record.ProjectorName,
                        newVersion,
                        typeof(SerializableMultiProjectionStateEnvelope).FullName!,
                        record.LastSortableUniqueId,
                        record.EventsProcessed,
                        modifiedBytes,
                        record.IsOffloaded,
                        record.OffloadKey,
                        record.OffloadProvider,
                        record.OriginalSizeBytes,
                        record.CompressedSizeBytes,
                        record.SafeWindowThreshold,
                        record.CreatedAt,
                        DateTime.UtcNow,
                        record.BuildSource,
                        record.BuildHost);

                    var saveResult = await _multiProjectionStateStore.UpsertAsync(modifiedRecord);
                    if (saveResult.IsSuccess)
                    {
                        updated = true;
                    }
                }
            }

            // Update Orleans ProjectorVersion field
            if (updated && _state.State != null)
            {
                _state.State.ProjectorVersion = newVersion;
                await _state.WriteStateAsync();
            }

            return updated;
        }
        catch (Exception ex)
        {
            _lastError = $"OverwritePersistedStateVersion failed: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> DeleteExternalStateAsync()
    {
        if (_multiProjectionStateStore == null || _host == null) return false;
        var projectorName = GetProjectorName();
        var projectorVersion = _host.GetProjectorVersion();
        var result = await _multiProjectionStateStore.DeleteAsync(projectorName, projectorVersion);
        return result.IsSuccess && result.GetValue();
    }

    public async Task SeedEventsAsync(IReadOnlyList<Event> events)
    {
        if (_eventStore == null) return;
        var result = await _eventStore.WriteEventsAsync(events);
        if (!result.IsSuccess)
        {
            throw result.GetException();
        }
    }

    private async Task FallbackEventCheckAsync()
    {
        // Only run fallback if we haven't received events recently
        if (_lastEventTime == null || DateTime.UtcNow - _lastEventTime > TimeSpan.FromMinutes(1))
        {
            _logger.LogWarning(
                "[{ProjectorName}] Fallback: No stream events for over 1 minute, checking event store",
                GetProjectorName());
            await RefreshAsync();
        }
    }

    private async Task CatchUpFromEventStoreAsync(bool forceFull = false)
    {
        // Legacy method for compatibility - now triggers timer-based catch-up
        if (_host == null || _eventStore == null) return;

        // If catch-up is already active, skip
        if (_catchUpProgress.IsActive)
        {
            return;
        }

        // Start timer-based catch-up if needed
        await InitiateCatchUpIfNeeded(forceFull);
    }

    private async Task InitiateCatchUpIfNeeded(bool forceFull = false)
    {
        var projectorName = GetProjectorName();

        // Double-check: If catch-up is already active, skip immediately
        // This prevents race conditions when multiple requests arrive concurrently
        if (_catchUpProgress.IsActive)
        {
            _logger.LogDebug("[{ProjectorName}] Catch-up already active, skipping initiation", projectorName);
            return;
        }

        // Mark as initiating early to prevent concurrent initiations
        _catchUpProgress.IsActive = true;

        try
        {
            // Get current position (fast operation - reads from local state)
            SortableUniqueId? currentPosition = forceFull ? null : await GetCurrentPositionAsync();

            // NOTE: We intentionally skip reading all events to determine target position.
            // Reading 200k+ events just to find the latest position causes activation timeout.
            // Instead, we start catch-up immediately and let it run until no new events are found.
            // The target position will be updated dynamically during catch-up batches.

            // Initialize catch-up progress (TargetPosition will be set during first batch)
            _catchUpProgress = new CatchUpProgress
            {
                CurrentPosition = currentPosition,
                TargetPosition = null, // Will be determined during catch-up
                IsActive = true,
                ConsecutiveEmptyBatches = 0,
                BatchesProcessed = 0,
                StartTime = DateTime.UtcNow,
                LastAttempt = DateTime.MinValue
            };

            MoveBufferedStreamEventsToPending(currentPosition);

            _logger.LogDebug(
                "[{ProjectorName}] Starting catch-up from {StartPosition} (target position will be determined dynamically)",
                projectorName,
                currentPosition?.Value ?? "beginning");

            // Start catch-up timer
            StartCatchUpTimer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ProjectorName}] Error during catch-up initiation", projectorName);
            _catchUpProgress.IsActive = false;
            throw;
        }
    }

    private void StartCatchUpTimer()
    {
        if (_catchUpTimer != null)
        {
            return; // Timer already running
        }

        var projectorName = GetProjectorName();
        _logger.LogDebug(
            "[{ProjectorName}] Starting catch-up timer with interval: {IntervalMs}ms",
            projectorName,
            _catchUpInterval.TotalMilliseconds);

        _catchUpTimer = this.RegisterGrainTimer(
            async () => await ProcessCatchUpBatchAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.Zero, // Start immediately
                Period = _catchUpInterval,
                Interleave = true
            });
    }

    private async Task ProcessCatchUpBatchAsync()
    {
        if (!_catchUpProgress.IsActive)
        {
            _catchUpTimer?.Dispose();
            _catchUpTimer = null;
            return;
        }

        var projectorName = GetProjectorName();
        var lockAcquired = await CatchUpBatchSemaphore.WaitAsync(TimeSpan.Zero);
        if (!lockAcquired)
        {
            _logger.LogDebug("[{ProjectorName}] Catch-up batch skipped due to global catch-up concurrency limit", projectorName);
            return;
        }

        try
        {
            // Process one batch
            var processed = await ProcessSingleCatchUpBatch();

            if (processed == 0)
            {
                _catchUpProgress.ConsecutiveEmptyBatches++;
                if (_catchUpProgress.ConsecutiveEmptyBatches >= MaxConsecutiveEmptyBatches)
                {
                    // Catch-up complete
                    await CompleteCatchUp();
                }
            }
            else
            {
                _catchUpProgress.ConsecutiveEmptyBatches = 0;
                _catchUpProgress.BatchesProcessed++;
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Catch-up batch failed: {ex.Message}";
            _logger.LogError(ex, "[{ProjectorName}] Catch-up batch error", projectorName);
            // Continue with next timer execution
        }
        finally
        {
            CatchUpBatchSemaphore.Release();
        }
    }

    private async Task<int> ProcessSingleCatchUpBatch()
    {
        if (_host == null || _eventStore == null) return 0;

        var projectorName = GetProjectorName();

        // Always use small batch size to avoid blocking
        var batchSize = _catchUpBatchSize;

        // Read batch of events
        var eventsResult = _catchUpProgress.CurrentPosition == null
            ? await _eventStore.ReadAllEventsAsync(since: null, maxCount: batchSize)
            : await _eventStore.ReadAllEventsAsync(since: _catchUpProgress.CurrentPosition.Value, maxCount: batchSize);

        if (!eventsResult.IsSuccess)
        {
            _logger.LogError(
                eventsResult.GetException(),
                "[{ProjectorName}] Failed to read events",
                projectorName);
            return 0;
        }

        var events = eventsResult.GetValue().ToList();
        if (events.Count == 0)
        {
            return 0;
        }

        // Update target position dynamically based on latest event in this batch
        // This avoids reading all events upfront just to determine the target
        var latestInBatch = events.Last();
        if (_catchUpProgress.TargetPosition == null ||
            string.Compare(latestInBatch.SortableUniqueIdValue, _catchUpProgress.TargetPosition.Value, StringComparison.Ordinal) > 0)
        {
            _catchUpProgress.TargetPosition = new SortableUniqueId(latestInBatch.SortableUniqueIdValue);
        }

        // Filter out already processed events
        var filtered = new List<Event>();
        foreach (var ev in events)
        {
            // Skip if already processed
            if (_processedEventIds.Contains(ev.Id.ToString()))
            {
                continue;
            }

            // Skip if before current position
            if (_catchUpProgress.CurrentPosition != null &&
                string.Compare(ev.SortableUniqueIdValue, _catchUpProgress.CurrentPosition.Value, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            filtered.Add(ev);
        }

        if (filtered.Count == 0)
        {
            // Update position even if no new events
            if (events.Count > 0)
            {
                var lastEvent = events.Last();
                _catchUpProgress.CurrentPosition = new SortableUniqueId(lastEvent.SortableUniqueIdValue);
            }
            return 0;
        }

        // Pass all filtered events to the host (host handles safe/unsafe separation internally)
        if (filtered.Count > 0)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogDebug(
                "[{ProjectorName}] Catch-up: Processing {EventCount} events",
                projectorName,
                filtered.Count);
            await _host.AddEventsFromCatchUpAsync(filtered, false);
            sw.Stop();
            _eventsProcessed += filtered.Count;

            if (sw.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning(
                    "[{ProjectorName}] AddEventsFromCatchUpAsync took {ElapsedMs}ms for {EventCount} events",
                    projectorName,
                    sw.ElapsedMilliseconds,
                    filtered.Count);
            }

            // Mark as processed
            foreach (var ev in filtered)
            {
                _processedEventIds.Add(ev.Id.ToString());
            }
        }

        // Update position
        var lastProcessed = filtered.Last();
        _catchUpProgress.CurrentPosition = new SortableUniqueId(lastProcessed.SortableUniqueIdValue);

        // Periodic persistence - only persist every 5000 events during catch-up
        if (_eventsProcessed > 0 && _eventsProcessed % 5000 == 0)
        {
            _logger.LogDebug(
                "[{ProjectorName}] Persisting state at {EventsProcessed:N0} events",
                projectorName,
                _eventsProcessed);
            await PersistStateAsync();
        }

        // Log progress only every 10 batches to reduce log spam
        if (_catchUpProgress.BatchesProcessed % 10 == 0 || filtered.Count == 0)
        {
            var elapsed = DateTime.UtcNow - _catchUpProgress.StartTime;
            var eventsPerSecond = _eventsProcessed > 0 && elapsed.TotalSeconds > 0
                ? (_eventsProcessed / elapsed.TotalSeconds).ToString("F0")
                : "0";
            _logger.LogDebug(
                "[{ProjectorName}] Catch-up: Batch #{BatchNumber}, {EventsProcessed:N0} events ({EventsPerSecond}/sec), elapsed: {ElapsedSeconds:F1}s",
                projectorName,
                _catchUpProgress.BatchesProcessed,
                _eventsProcessed,
                eventsPerSecond,
                elapsed.TotalSeconds);
        }

        return filtered.Count;
    }

    private async Task CompleteCatchUp()
    {
        var projectorName = GetProjectorName();

        // Stop timer
        _catchUpTimer?.Dispose();
        _catchUpTimer = null;

        // Process all buffered events first
        await FlushEventBufferAsync();

        // Force promotion of any events that are now safe
        await TriggerSafePromotion();

        // Final persistence
        await PersistStateAsync();

        // Process any pending stream events
        await ProcessPendingStreamEvents();

        _catchUpProgress.IsActive = false;

        var elapsed = DateTime.UtcNow - _catchUpProgress.StartTime;
        _logger.LogDebug(
            "[{ProjectorName}] Catch-up completed: {BatchCount} batches, {EventsProcessed:N0} events, elapsed: {ElapsedSeconds:F1}s",
            projectorName,
            _catchUpProgress.BatchesProcessed,
            _eventsProcessed,
            elapsed.TotalSeconds);
    }

    private async Task TriggerSafePromotion()
    {
        try
        {
            if (_host != null)
            {
                var projectorName = GetProjectorName();
                _logger.LogDebug(
                    "[{ProjectorName}] Triggering safe promotion check after catch-up",
                    projectorName);

                // Get the current safe state to trigger promotion
                var safeState = await _host.GetStateAsync(canGetUnsafeState: false);
                if (safeState.IsSuccess)
                {
                    var state = safeState.GetValue();
                    _logger.LogDebug(
                        "[{ProjectorName}] Safe state after promotion: version={StateVersion}",
                        projectorName,
                        state.Version);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ProjectorName}] Error during safe promotion", GetProjectorName());
        }
    }

    private async Task ProcessPendingStreamEvents()
    {
        if (_pendingStreamEvents.Count == 0) return;

        var projectorName = GetProjectorName();
        var events = new List<SerializableEvent>();

        while (_pendingStreamEvents.Count > 0)
        {
            var ev = _pendingStreamEvents.Dequeue();
            if (_processedEventIds.Contains(ev.Id.ToString()))
            {
                continue;
            }
            events.Add(ev);
        }

        if (events.Count == 0)
        {
            return;
        }

        _logger.LogDebug(
            "[{ProjectorName}] Processing {EventCount} pending stream events",
            projectorName,
            events.Count);

        // Process all events via host - host handles safe/unsafe internally
        var allEvents = events.OrderBy(e => e.SortableUniqueIdValue).ToList();
        if (allEvents.Count > 0 && _host != null)
        {
            await _host.AddSerializableEventsAsync(allEvents, true);
            _eventsProcessed += allEvents.Count;
            foreach (var ev in allEvents)
            {
                _processedEventIds.Add(ev.Id.ToString());
            }
            _lastEventTime = DateTime.UtcNow;
            if (_state.State != null)
            {
                _state.State.LastPosition = allEvents.Last().SortableUniqueIdValue;
            }
        }
    }

    private async Task<SortableUniqueId?> GetCurrentPositionAsync()
    {
        if (_host == null) return null;

        // Try to get from current state
        var currentState = await _host.GetStateAsync(canGetUnsafeState: false);
        if (currentState.IsSuccess)
        {
            var state = currentState.GetValue();
            if (!string.IsNullOrEmpty(state.LastSortableUniqueId))
            {
                return new SortableUniqueId(state.LastSortableUniqueId);
            }
        }

        return null;
    }

    private SortableUniqueId GetSafeWindowThreshold()
    {
        // Use actor's safe window calculation if available
        var now = DateTime.UtcNow;
        var safeWindowMs = _injectedActorOptions?.SafeWindowMs ?? 20000;
        var threshold = now.AddMilliseconds(-safeWindowMs);
        return SortableUniqueId.Generate(threshold, Guid.Empty);
    }

    private void EnqueuePendingStreamEvents(IEnumerable<SerializableEvent> events, SortableUniqueId? currentPosition)
    {
        var buffered = 0;
        foreach (var ev in events)
        {
            if (currentPosition != null)
            {
                var eventPos = new SortableUniqueId(ev.SortableUniqueIdValue);
                if (!eventPos.IsLaterThan(currentPosition))
                {
                    continue;
                }
            }
            _pendingStreamEvents.Enqueue(ev);
            buffered++;
        }

        if (buffered > 0)
        {
            _logger.LogDebug(
                "[{ProjectorName}] Buffered {BufferedCount} stream events during catch-up (queue size: {QueueSize})",
                GetProjectorName(),
                buffered,
                _pendingStreamEvents.Count);
        }

        if (_maxPendingStreamEvents > 0)
        {
            while (_pendingStreamEvents.Count > _maxPendingStreamEvents)
            {
                _pendingStreamEvents.Dequeue();
            }
        }
    }

    private void MoveBufferedStreamEventsToPending(SortableUniqueId? currentPosition)
    {
        List<SerializableEvent> buffered;
        lock (_eventBuffer)
        {
            if (_eventBuffer.Count == 0) return;
            buffered = new List<SerializableEvent>(_eventBuffer);
            _eventBuffer.Clear();
            _unsafeEventIds.Clear();
        }

        EnqueuePendingStreamEvents(buffered, currentPosition);
    }

    /// <summary>
    ///     Process a batch of serializable events from the stream
    /// </summary>
    internal async Task ProcessEventBatch(IReadOnlyList<SerializableEvent> events)
    {
        if (!_isInitialized || _host == null)
        {
            await EnsureInitializedAsync();
        }

        if (_host == null || events.Count == 0) return;

        try
        {
            _logger.LogDebug(
                "[{ProjectorName}] Stream batch received: {EventCount} events",
                GetProjectorName(),
                events.Count);

            // If catch-up is active, buffer events for later
            if (_catchUpProgress.IsActive)
            {
                var buffered = 0;
                foreach (var ev in events)
                {
                    var eventPos = new SortableUniqueId(ev.SortableUniqueIdValue);

                    // Only buffer events that are newer than our catch-up position
                    if (_catchUpProgress.CurrentPosition == null ||
                        eventPos.IsLaterThan(_catchUpProgress.CurrentPosition))
                    {
                        _pendingStreamEvents.Enqueue(ev);
                        buffered++;
                    }
                    // Else: duplicate event that will be caught up, ignore
                }

                if (buffered > 0)
                {
                    _logger.LogDebug(
                        "[{ProjectorName}] Buffered {BufferedCount} stream events during catch-up (queue size: {QueueSize})",
                        GetProjectorName(),
                        buffered,
                        _pendingStreamEvents.Count);
                }

                // Limit buffer size to prevent memory issues
                const int MaxPendingEvents = 50000;
                while (_pendingStreamEvents.Count > MaxPendingEvents)
                {
                    _pendingStreamEvents.Dequeue();
                }

                return;
            }

            // Normal processing mode - filter and process
            var newEvents = events.Where(e => !_processedEventIds.Contains(e.Id.ToString())).ToList();

            if (newEvents.Count > 0)
            {
                // Delegate to host - host handles safe/unsafe internally
                await _host.AddSerializableEventsAsync(newEvents, true);
                _eventsProcessed += newEvents.Count;

                // Mark all events as processed
                foreach (var ev in newEvents)
                {
                    _processedEventIds.Add(ev.Id.ToString());
                }

                _lastEventTime = DateTime.UtcNow;
            }

            // Update position to the maximum SortableUniqueId in the batch (monotonic)
            var maxSortableId = events
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
                .Last()
                .SortableUniqueIdValue;
            _state.State.LastPosition = maxSortableId;

            _logger.LogDebug(
                "[{ProjectorName}] Processed {EventCount} events - Total: {EventsProcessed:N0} events",
                GetProjectorName(),
                newEvents.Count,
                _eventsProcessed);

            // Persist state after processing a batch if it's large enough
            if (newEvents.Count >= _persistBatchSize)
            {
                await PersistStateAsync();
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to process event batch: {ex.Message}";
            _logger.LogError(ex, "[{ProjectorName}] Error processing event batch", GetProjectorName());

            // Log inner exception for better debugging
            if (ex.InnerException != null)
            {
                _logger.LogError(
                    ex.InnerException,
                    "[{ProjectorName}] Inner exception during event batch processing",
                    GetProjectorName());
                _lastError += $" Inner: {ex.InnerException.Message}";
            }
        }
    }

    /// <summary>
    ///     Process buffered events - called by timer
    /// </summary>
    private async Task FlushEventBufferAsync()
    {
        List<SerializableEvent> eventsToProcess;
        lock (_eventBuffer)
        {
            if (_eventBuffer.Count == 0)
            {
                // Even if buffer is empty, trigger safe promotion periodically
                // to ensure events transition from unsafe to safe over time
                eventsToProcess = new List<SerializableEvent>();
            }
            else
            {
                eventsToProcess = new List<SerializableEvent>(_eventBuffer);
                _eventBuffer.Clear();
                _unsafeEventIds.Clear();
                _lastBufferFlush = DateTime.UtcNow;
            }
        }

        if (eventsToProcess.Count > 0)
        {
            await ProcessBufferedSerializableEvents(eventsToProcess);
        }
        else
        {
            // Even if no events to process, trigger safe promotion
            await TriggerSafePromotion();
        }
    }

    /// <summary>
    ///     Process buffered serializable events via host
    /// </summary>
    private async Task ProcessBufferedSerializableEvents(List<SerializableEvent> events)
    {
        if (_host == null || events.Count == 0) return;

        try
        {
            var projectorName = GetProjectorName();

            // Delegate to host - host handles safe/unsafe internally
            _logger.LogDebug(
                "[{ProjectorName}] Processing {EventCount} buffered events",
                projectorName,
                events.Count);
            await _host.AddSerializableEventsAsync(events, true);
            _eventsProcessed += events.Count;

            foreach (var ev in events)
            {
                _processedEventIds.Add(ev.Id.ToString());
            }

            // Update position
            var maxSortableId = events
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
                .Last()
                .SortableUniqueIdValue;
            _state.State.LastPosition = maxSortableId;

            _logger.LogDebug(
                "[{ProjectorName}] Processed {EventCount} buffered events - Total: {EventsProcessed:N0} events",
                projectorName,
                events.Count,
                _eventsProcessed);

            // Trigger safe promotion after processing buffered events
            // This ensures that events transition from unsafe to safe as time passes
            await TriggerSafePromotion();
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to process buffered events: {ex.Message}";
            _logger.LogError(ex, "[{ProjectorName}] Error processing buffered events", GetProjectorName());
        }
    }

    // Orleans stream batch observer - passes SerializableEvent directly to the Grain without deserialization
    private class StreamBatchObserver : IAsyncBatchObserver<SerializableEvent>
    {
        private readonly MultiProjectionGrain _grain;

        public StreamBatchObserver(MultiProjectionGrain grain) => _grain = grain;

        // Batch processing method - Orleans v9.0+ uses IList<SequentialItem<T>>
        public Task OnNextAsync(IList<SequentialItem<SerializableEvent>> batch)
        {
            var events = batch.Select(item => item.Item).ToList();
            _grain._logger.LogDebug(
                "[StreamBatchObserver-{ProjectorName}] Received batch of {EventCount} events",
                _grain.GetProjectorName(),
                events.Count);
            _grain.EnqueueStreamEvents(events);
            return Task.CompletedTask;
        }

        // Legacy batch method for compatibility
        public Task OnNextBatchAsync(IEnumerable<SerializableEvent> batch, StreamSequenceToken? token = null)
        {
            var events = batch.ToList();
            _grain._logger.LogDebug(
                "[StreamBatchObserver-{ProjectorName}] Received legacy batch of {EventCount} events",
                _grain.GetProjectorName(),
                events.Count);
            _grain.EnqueueStreamEvents(events);
            return Task.CompletedTask;
        }

        public Task OnNextAsync(SerializableEvent item, StreamSequenceToken? token = null)
        {
            _grain._logger.LogDebug(
                "[StreamBatchObserver-{ProjectorName}] Received single event {EventType}, ID: {EventId}",
                _grain.GetProjectorName(),
                item.EventPayloadName,
                item.Id);
            _grain.EnqueueStreamEvents(new[] { item });
            return Task.CompletedTask;
        }

        public Task OnCompletedAsync()
        {
            _grain._logger.LogDebug(
                "[StreamBatchObserver-{ProjectorName}] Stream completed",
                _grain.GetProjectorName());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _grain._logger.LogError(
                ex,
                "[StreamBatchObserver-{ProjectorName}] Stream error",
                _grain.GetProjectorName());
            _grain._lastError = $"Stream error: {ex.Message}";
            return Task.CompletedTask;
        }
    }

    // (removed test-only projector tag scoping)

    internal void EnqueueStreamEvents(IEnumerable<SerializableEvent> events)
    {
        var list = events as IList<SerializableEvent> ?? events.ToList();
        if (list.Count == 0) return;

        if (_catchUpProgress.IsActive)
        {
            EnqueuePendingStreamEvents(list, _catchUpProgress.CurrentPosition);
            _lastEventTime = DateTime.UtcNow;
            return;
        }

        var newEvents = list.Where(e => !_processedEventIds.Contains(e.Id.ToString())).ToList();
        if (newEvents.Count == 0) return;
        list = newEvents;

        lock (_eventBuffer)
        {
            foreach (var ev in list)
            {
                _eventBuffer.Add(ev);
            }
        }
        _lastEventTime = DateTime.UtcNow;
        // Do not record deliveries here to avoid double-counting.
        // Delivery statistics are recorded after successful processing
        // inside ProcessEventBatch.

        // Schedule a near-immediate flush to avoid long lag before first timer tick
        if (_immediateFlushTimer == null)
        {
            _immediateFlushTimer = this.RegisterGrainTimer(
                async () =>
                {
                    try { await FlushEventBufferAsync(); }
                    finally
                    {
                        _immediateFlushTimer?.Dispose();
                        _immediateFlushTimer = null;
                    }
                },
                new GrainTimerCreationOptions
                {
                    DueTime = TimeSpan.FromMilliseconds(5),
                    Period = Timeout.InfiniteTimeSpan,
                    Interleave = true
                });
        }
    }

    #region ILifecycleParticipant
    public void Participate(IGrainLifecycle lifecycle)
    {
        _logger.LogDebug("[SimplifiedPureGrain] Participate called - registering lifecycle stage");
        var stage = GrainLifecycleStage.Activate + 100;
        lifecycle.Subscribe(GetType().FullName!, stage, InitStreamsAsync, CloseStreamsAsync);
        _logger.LogDebug("[SimplifiedPureGrain] Lifecycle stage registered at {Stage}", stage);
    }

    private async Task InitStreamsAsync(CancellationToken ct)
    {
        var grainKey = GetGrainKey();
        var projectorName = GetProjectorName();
        _logger.LogDebug("[SimplifiedPureGrain-{ProjectorName}] InitStreamsAsync called in lifecycle stage", projectorName);

        var streamInfo = _subscriptionResolver.Resolve(grainKey);
        if (streamInfo is not OrleansSekibanStream orleansStream)
        {
            throw new InvalidOperationException($"Invalid stream type: {streamInfo?.GetType().Name}");
        }

        var streamProvider = this.GetStreamProvider(orleansStream.ProviderName);
        _orleansStream = streamProvider.GetStream<SerializableEvent>(
            StreamId.Create(orleansStream.StreamNamespace, orleansStream.StreamId));
        _logger.LogDebug("[SimplifiedPureGrain-{ProjectorName}] Stream prepared", projectorName);
    }

    private async Task CloseStreamsAsync(CancellationToken ct)
    {
        try
        {
            if (_orleansStream != null)
            {
                var handles = await _orleansStream.GetAllSubscriptionHandles();
                foreach (var h in handles)
                {
                    try { await h.UnsubscribeAsync(); }
                    catch { /* ignore */ }
                }
            }
            else if (_orleansStreamHandle != null)
            {
                await _orleansStreamHandle.UnsubscribeAsync();
            }
        }
        finally
        {
            _orleansStreamHandle = null;
        }
    }
    #endregion
}
