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
using Sekiban.Dcb.ColdEvents;
using System.Text;
using System.Runtime;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Simplified pure infrastructure grain with minimal business logic
///     Demonstrates separation of concerns
/// </summary>
public class MultiProjectionGrain : Grain, IMultiProjectionGrain, ILifecycleParticipant<IGrainLifecycle>
{
    private const int StreamingCatchUpApplyChunkSize = 4096;
    private const string EmptyLogValue = "empty";
    private readonly IProjectionActorHostFactory _actorHostFactory;
    private readonly IEventStore _eventStore;
    private readonly IPersistentState<MultiProjectionGrainState> _state;
    private readonly IEventSubscriptionResolver _subscriptionResolver;
    private readonly IMultiProjectionStateStore? _multiProjectionStateStore;
    private readonly GeneralMultiProjectionActorOptions? _injectedActorOptions;
    private readonly ILogger<MultiProjectionGrain> _logger;
    private readonly IEventStoreFactory? _eventStoreFactory;
    private readonly IServiceIdProvider _serviceIdProvider;
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
    private readonly HashSet<Guid> _processedEventIds = new(); // Track processed event IDs to prevent double counting
    private readonly Queue<Guid> _processedEventIdOrder = new();
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
        public bool HadNewEvents { get; set; }
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
    private static readonly GeneralMultiProjectionActorOptions DefaultActorOptions = new();
    private readonly TimeSpan _catchUpInterval = TimeSpan.FromSeconds(1); // Standard interval after performance fix
    private TimeSpan _catchUpDeactivationDelay = TimeSpan.FromMinutes(10);
    private int _catchUpMaxConsecutiveFailures = 120;
    private TimeSpan _catchUpMaxFailureDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CatchUpStallThreshold = TimeSpan.FromSeconds(15);
    private static readonly SemaphoreSlim CatchUpBatchSemaphore = new(1, 1);
    private bool _catchUpDeactivationDelayActive;
    private int _catchUpConsecutiveFailureCount;
    private DateTime? _catchUpFailureWindowStartUtc;

    // Delegate these to configuration
    private int _persistBatchSize = DefaultActorOptions.PersistBatchSize; // Persist less frequently to avoid blocking deliveries
    private TimeSpan _persistInterval = TimeSpan.FromSeconds(DefaultActorOptions.PersistIntervalSeconds);
    private bool _skipPersistWhenSafeCheckpointUnchanged = true;
    private readonly TimeSpan _fallbackCheckInterval = TimeSpan.FromSeconds(30);
    private int _maxPendingStreamEvents = 50000;
    private int _catchUpBatchSize = DefaultCatchUpBatchSize;
    private int _processedEventIdCacheSize = 200000;
    private bool _forceGcAfterLargeSnapshotPersist = true;
    private long _largeSnapshotGcThresholdBytes = LargePayloadThresholdBytes;

    private IEventStore? _resolvedCatchUpEventStore;
    private bool _useStreamingSnapshotIO;
    private readonly TempFileSnapshotManager? _tempFileSnapshotManager;
    private bool _hybridCatchUpCheckLogged;
    private bool _hybridCatchUpFirstBatchLogged;
    private HybridReadBatchMetadata? _lastHybridReadBatchMetadata;
    private long _eventsProcessedSinceLastCatchUpPersist;
    private DateTime _lastCatchUpPersistUtc = DateTime.UtcNow;

    private sealed record PersistPolicySettings(
        int PersistBatchSize,
        TimeSpan PersistInterval,
        bool SkipPersistWhenSafeCheckpointUnchanged);

    private sealed record PersistCheckpoint(
        string ProjectorVersion,
        string? SafePosition,
        int? SafeVersion,
        int? UnsafeVersion,
        string? SafeThresholdValue,
        DateTime? SafeThresholdTime);

    private sealed record StreamingExternalStorePersistResult(
        bool ExternalStoreSaved,
        long UploadElapsedMs);

    public MultiProjectionGrain(
        [PersistentState("multiProjection", "OrleansStorage")] IPersistentState<MultiProjectionGrainState> state,
        IProjectionActorHostFactory actorHostFactory,
        IEventStore eventStore,
        IEventSubscriptionResolver? subscriptionResolver,
        IMultiProjectionStateStore? multiProjectionStateStore,
        Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics? eventStats,
        GeneralMultiProjectionActorOptions? actorOptions,
        TempFileSnapshotManager? tempFileSnapshotManager = null,
        ILogger<MultiProjectionGrain>? logger = null,
        IEventStoreFactory? eventStoreFactory = null,
        IServiceIdProvider? serviceIdProvider = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _actorHostFactory = actorHostFactory ?? throw new ArgumentNullException(nameof(actorHostFactory));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _subscriptionResolver = subscriptionResolver ?? new DefaultOrleansEventSubscriptionResolver();
        _multiProjectionStateStore = multiProjectionStateStore;
        _eventStats = eventStats ?? new Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics();
        _injectedActorOptions = actorOptions;
        _tempFileSnapshotManager = tempFileSnapshotManager;
        _logger = logger ?? NullLogger<MultiProjectionGrain>.Instance;
        _eventStoreFactory = eventStoreFactory;
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();

        if (_injectedActorOptions is not null)
        {
            _persistBatchSize = Math.Max(0, _injectedActorOptions.PersistBatchSize);
            _persistInterval = _injectedActorOptions.PersistIntervalSeconds > 0
                ? TimeSpan.FromSeconds(_injectedActorOptions.PersistIntervalSeconds)
                : TimeSpan.Zero;
            _skipPersistWhenSafeCheckpointUnchanged = _injectedActorOptions.SkipPersistWhenSafeCheckpointUnchanged;
        }
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

    private PersistPolicySettings ResolvePersistPolicySettings(string projectorName)
    {
        var options = _injectedActorOptions ?? DefaultActorOptions;

        var persistBatchSize = options.PersistBatchSize;
        var persistIntervalSeconds = options.PersistIntervalSeconds;
        var skipPersistWhenUnchanged = options.SkipPersistWhenSafeCheckpointUnchanged;

        if (options.ProjectorPersistenceOverrides != null &&
            options.ProjectorPersistenceOverrides.TryGetValue(projectorName, out var projectorOverride))
        {
            if (projectorOverride.PersistBatchSize.HasValue)
            {
                persistBatchSize = projectorOverride.PersistBatchSize.Value;
            }

            if (projectorOverride.PersistIntervalSeconds.HasValue)
            {
                persistIntervalSeconds = projectorOverride.PersistIntervalSeconds.Value;
            }

            if (projectorOverride.SkipPersistWhenSafeCheckpointUnchanged.HasValue)
            {
                skipPersistWhenUnchanged = projectorOverride.SkipPersistWhenSafeCheckpointUnchanged.Value;
            }
        }

        persistBatchSize = Math.Max(0, persistBatchSize);
        persistIntervalSeconds = Math.Max(0, persistIntervalSeconds);

        return new PersistPolicySettings(
            PersistBatchSize: persistBatchSize,
            PersistInterval: persistIntervalSeconds > 0
                ? TimeSpan.FromSeconds(persistIntervalSeconds)
                : TimeSpan.Zero,
            SkipPersistWhenSafeCheckpointUnchanged: skipPersistWhenUnchanged);
    }

    private void ApplyPersistPolicySettings(string projectorName)
    {
        var settings = ResolvePersistPolicySettings(projectorName);
        _persistBatchSize = settings.PersistBatchSize;
        _persistInterval = settings.PersistInterval;
        _skipPersistWhenSafeCheckpointUnchanged = settings.SkipPersistWhenSafeCheckpointUnchanged;
    }

    private static string FormatLogValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return EmptyLogValue;
        }

        return value.Length > 20 ? value[..20] : value;
    }

    private void ResetHybridCatchUpLogging()
    {
        _hybridCatchUpCheckLogged = false;
        _hybridCatchUpFirstBatchLogged = false;
        _lastHybridReadBatchMetadata = null;
    }

    /// <summary>
    ///     Returns the event store used for catch-up reads.
    ///     Preference order:
    ///     1) Injected HybridEventStore when ServiceIdProvider matches grain ServiceId
    ///        (keeps cold + hot merge in one read path).
    ///     2) IEventStoreFactory-created ServiceId-scoped store when available.
    ///     3) Injected IEventStore fallback.
    ///     The result is cached for the grain's lifetime after first resolution.
    /// </summary>
    private IEventStore GetCatchUpEventStore()
    {
        if (_resolvedCatchUpEventStore != null)
            return _resolvedCatchUpEventStore;

        // Ensure _serviceId is parsed from grain key before resolving catch-up store.
        GetIdentity();

        // When cold-event hybrid read is configured, keep using the injected IEventStore
        // so catch-up reads can merge cold segments + hot tail in one path.
        if (_eventStore is HybridEventStore)
        {
            var currentServiceId = _serviceIdProvider.GetCurrentServiceId();
            if (!string.Equals(currentServiceId, _serviceId, StringComparison.Ordinal))
            {
                if (_eventStoreFactory != null)
                {
                    _resolvedCatchUpEventStore = _eventStoreFactory.CreateForService(_serviceId);
                    _logger.LogWarning(
                        "[{ProjectorName}] ServiceIdProvider returned {CurrentServiceId}, but grain ServiceId is {GrainServiceId}. " +
                        "Using factory-created ServiceId-scoped store for catch-up.",
                        GetProjectorName(),
                        currentServiceId,
                        _serviceId);
                    return _resolvedCatchUpEventStore;
                }

                _logger.LogWarning(
                    "[{ProjectorName}] ServiceIdProvider returned {CurrentServiceId}, but grain ServiceId is {GrainServiceId}. " +
                    "Falling back to injected hybrid store because no factory is available.",
                    GetProjectorName(),
                    currentServiceId,
                    _serviceId);
            }

            _resolvedCatchUpEventStore = _eventStore;
            _logger.LogDebug(
                "[{ProjectorName}] Using injected hybrid event store for catch-up (cold + hot, ServiceId={ServiceId})",
                GetProjectorName(),
                _serviceId);
            return _resolvedCatchUpEventStore;
        }

        if (_eventStoreFactory != null)
        {
            _resolvedCatchUpEventStore = _eventStoreFactory.CreateForService(_serviceId);
            _logger.LogDebug(
                "[{ProjectorName}] Using factory-created event store for catch-up (ServiceId={ServiceId})",
                GetProjectorName(),
                _serviceId);
        }
        else
        {
            _resolvedCatchUpEventStore = _eventStore;
        }

        return _resolvedCatchUpEventStore;
    }

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

    private async Task<PersistCheckpoint> CapturePersistCheckpointAsync(string projectorName)
    {
        int? safeVersion = null;
        int? unsafeVersion = null;
        try
        {
            var metadataResult = await _host!.GetStateMetadataAsync(includeUnsafe: true);
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
                    FormatLogValue(safeLastId),
                    metadata.UnsafeVersion,
                    FormatLogValue(unsafeLastId));
            }
        }
        catch
        {
            // Metadata is best-effort for diagnostics and skip detection.
        }

        string? safeThresholdValue = null;
        DateTime? safeThresholdTime = null;
        try
        {
            var candidateSafeThreshold = _host!.PeekCurrentSafeWindowThreshold();
            var safeThresholdId = new SortableUniqueId(candidateSafeThreshold);
            safeThresholdValue = candidateSafeThreshold;
            safeThresholdTime = safeThresholdId.GetDateTime();
        }
        catch
        {
            // Safe-threshold diagnostics are optional.
        }

        return new PersistCheckpoint(
            ProjectorVersion: _host!.GetProjectorVersion(),
            SafePosition: await _host.GetSafeLastSortableUniqueIdAsync(),
            SafeVersion: safeVersion,
            UnsafeVersion: unsafeVersion,
            SafeThresholdValue: safeThresholdValue,
            SafeThresholdTime: safeThresholdTime);
    }

    private bool ShouldSkipPersistForUnchangedSafeCheckpoint(
        string projectorVersion,
        string? safePosition,
        int? safeVersion)
    {
        if (!_skipPersistWhenSafeCheckpointUnchanged || _state.State == null)
        {
            return false;
        }

        if (!string.Equals(_state.State.ProjectorVersion, projectorVersion, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(_state.State.LastSortableUniqueId ?? string.Empty, safePosition ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        if (safeVersion.HasValue)
        {
            return _state.State.LastGoodSafeVersion > 0 && safeVersion.Value == _state.State.LastGoodSafeVersion;
        }

        return true;
    }

    private ResultBox<bool>? TryShortCircuitPersist(string projectorName, PersistCheckpoint checkpoint)
    {
        var lastGoodSafeVersion = _state.State?.LastGoodSafeVersion ?? 0;
        if (checkpoint.SafeVersion.HasValue && lastGoodSafeVersion > 0 && checkpoint.SafeVersion.Value < lastGoodSafeVersion)
        {
            _lastError = $"Integrity guard blocked persist: safeVersion {checkpoint.SafeVersion.Value} < LastGoodSafeVersion {lastGoodSafeVersion}";
            _logger.LogError(
                MultiProjectionLogEvents.IntegrityGuardBlockedPersist,
                "BLOCKED persist: {ProjectorName} - safeVersion regression detected. Current={CurrentSafeVersion}, LastGood={LastGoodSafeVersion}. State will NOT be saved.",
                projectorName,
                checkpoint.SafeVersion.Value,
                lastGoodSafeVersion);
            _stateRestoreSource = StateRestoreSource.Failed;
            return ResultBox.FromValue(false);
        }

        if (!ShouldSkipPersistForUnchangedSafeCheckpoint(
                checkpoint.ProjectorVersion,
                checkpoint.SafePosition,
                checkpoint.SafeVersion))
        {
            return null;
        }

        _lastError = null;
        _logger.LogDebug(
            "[{ProjectorName}] Skipping persistence because the safe checkpoint is unchanged (ProjectorVersion={ProjectorVersion}, SafeVersion={SafeVersion}, SafePosition={SafePosition})",
            projectorName,
            checkpoint.ProjectorVersion,
            checkpoint.SafeVersion,
            FormatLogValue(checkpoint.SafePosition));
        return ResultBox.FromValue(true);
    }

    private async Task<bool> CanSaveToExternalStoreAsync(string projectorName, string projectorVersion)
    {
        if (_multiProjectionStateStore is null)
        {
            return false;
        }

        var latestResult = await _multiProjectionStateStore.GetLatestForVersionAsync(projectorName, projectorVersion);
        if (!latestResult.IsSuccess)
        {
            _lastError = $"External store read failed: {latestResult.GetException().Message}";
            _logger.LogWarning(
                "Skip external store save: failed to read latest state for {ProjectorName} v{ProjectorVersion}.",
                projectorName,
                projectorVersion);
            return false;
        }

        var latestOptional = latestResult.GetValue();
        if (latestOptional.HasValue &&
            latestOptional.Value is { } latestRecord &&
            latestRecord.EventsProcessed > _eventsProcessed)
        {
            _lastError = $"External store has newer state ({latestRecord.EventsProcessed}) than local ({_eventsProcessed})";
            _logger.LogWarning(
                "Skip external store save: latest EventsProcessed {LatestEvents} > local {LocalEvents} for {ProjectorName} v{ProjectorVersion}.",
                latestRecord.EventsProcessed,
                _eventsProcessed,
                projectorName,
                projectorVersion);
            return false;
        }

        return true;
    }

    private async Task<StreamingExternalStorePersistResult> SaveStreamingSnapshotToExternalStoreAsync(
        string projectorName,
        PersistCheckpoint checkpoint,
        string filePath,
        long tempFileSize)
    {
        if (_multiProjectionStateStore is null)
        {
            return new StreamingExternalStorePersistResult(true, 0);
        }

        var uploadStartMs = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!await CanSaveToExternalStoreAsync(projectorName, checkpoint.ProjectorVersion))
        {
            _logger.LogDebug("[{ProjectorName}] External store save skipped (store ahead or read failed)", projectorName);
            return new StreamingExternalStorePersistResult(
                ExternalStoreSaved: false,
                UploadElapsedMs: (long)System.Diagnostics.Stopwatch.GetElapsedTime(uploadStartMs).TotalMilliseconds);
        }

        var writeRequest = new MultiProjectionStateWriteRequest(
            ProjectorName: projectorName,
            ProjectorVersion: checkpoint.ProjectorVersion,
            PayloadType: typeof(SerializableMultiProjectionStateEnvelope).FullName!,
            LastSortableUniqueId: checkpoint.SafePosition ?? string.Empty,
            EventsProcessed: _eventsProcessed,
            IsOffloaded: false,
            OffloadKey: null,
            OffloadProvider: null,
            OriginalSizeBytes: tempFileSize,
            CompressedSizeBytes: tempFileSize,
            SafeWindowThreshold: checkpoint.SafeThresholdValue ?? _host!.PeekCurrentSafeWindowThreshold(),
            CreatedAt: _state.State!.LastPersistTime == default
                ? DateTime.UtcNow
                : _state.State.LastPersistTime,
            UpdatedAt: DateTime.UtcNow,
            BuildSource: "GRAIN_STREAM",
            BuildHost: Environment.MachineName);

        using var uploadStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var saveResult = await _multiProjectionStateStore.UpsertFromStreamAsync(
            writeRequest,
            uploadStream,
            _injectedActorOptions?.MaxSnapshotSerializedSizeBytes ?? 2 * 1024 * 1024,
            CancellationToken.None);
        if (!saveResult.IsSuccess)
        {
            _lastError = $"External store save failed: {saveResult.GetException().Message}";
            _logger.LogWarning("[{ProjectorName}] {LastError}", projectorName, _lastError);
            return new StreamingExternalStorePersistResult(
                ExternalStoreSaved: false,
                UploadElapsedMs: (long)System.Diagnostics.Stopwatch.GetElapsedTime(uploadStartMs).TotalMilliseconds);
        }

        return new StreamingExternalStorePersistResult(
            ExternalStoreSaved: true,
            UploadElapsedMs: (long)System.Diagnostics.Stopwatch.GetElapsedTime(uploadStartMs).TotalMilliseconds);
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

        await using var snapshotStream = new MemoryStream();
        var writeResult = await _host.WriteSnapshotToStreamAsync(
            snapshotStream,
            canGetUnsafeState,
            CancellationToken.None);
        if (!writeResult.IsSuccess)
            return ResultBox.Error<string>(writeResult.GetException());

        snapshotStream.Position = 0;
        using var reader = new StreamReader(snapshotStream, Encoding.UTF8, leaveOpen: true);
        var snapshotJson = await reader.ReadToEndAsync();
        return ResultBox.FromValue(snapshotJson);
    }

    public async Task AddEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true)
    {
        await EnsureInitializedAsync();

        if (_host == null)
        {
            throw new InvalidOperationException("Projection host not initialized");
        }

        // Filter out already processed events to prevent double counting
        var newEvents = events.Where(e => !_processedEventIds.Contains(e.Id)).ToList();

        if (newEvents.Count > 0)
        {
            await _host.AddSerializableEventsAsync(newEvents, finishedCatchUp);
            _eventsProcessed += newEvents.Count;

            // Mark events as processed
            foreach (var ev in newEvents)
            {
                TrackProcessedEventId(ev.Id);
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

            var checkpoint = await CapturePersistCheckpointAsync(projectorName);
            var shortCircuit = TryShortCircuitPersist(projectorName, checkpoint);
            if (shortCircuit is not null)
            {
                return shortCircuit;
            }

            // Use streaming path when enabled and temp file manager is available
            if (_useStreamingSnapshotIO && _tempFileSnapshotManager is not null)
            {
                return await PersistStateStreamingAsync(projectorName, checkpoint);
            }

            // Get snapshot as opaque bytes from the host
            await using var snapshotStream = new MemoryStream();
            var snapshotWriteResult = await _host.WriteSnapshotToStreamAsync(
                snapshotStream,
                canGetUnsafeState: false,
                CancellationToken.None);
            if (!snapshotWriteResult.IsSuccess)
            {
                _lastError = snapshotWriteResult.GetException().Message;
                _logger.LogWarning("[{ProjectorName}] {LastError}", projectorName, _lastError);
                return ResultBox.FromValue(false);
            }

            var envelopeSize = snapshotStream.Length;

            // Get metadata via host
            var safeVersion = checkpoint.SafeVersion;
            var unsafeVersion = checkpoint.UnsafeVersion;
            long originalSizeBytes = envelopeSize;
            long compressedSizeBytes = envelopeSize;
            var projectorVersion = checkpoint.ProjectorVersion;
            var safePosition = checkpoint.SafePosition;
            var safeThresholdTime = checkpoint.SafeThresholdTime;

            _logger.LogDebug(
                "[{ProjectorName}] v10: Writing snapshot: {EnvelopeSize:N0} bytes, {EventsProcessed:N0} events, checkpoint: {Checkpoint}",
                projectorName,
                envelopeSize,
                _eventsProcessed,
                FormatLogValue(safePosition));
            _logger.LogInformation(
                MultiProjectionLogEvents.PersistDetails,
                "Persist: {ProjectorName}, Events={EventsProcessed}, SafeVer={SafeVersion}, UnsafeVer={UnsafeVersion}, EnvelopeSize={EnvelopeSize}, SafeThreshold={SafeThreshold}",
                projectorName,
                _eventsProcessed,
                safeVersion,
                unsafeVersion,
                envelopeSize,
                safeThresholdTime);

            var externalStoreSaved = _multiProjectionStateStore == null;
            var allowExternalStoreSave = _multiProjectionStateStore is not null &&
                                         await CanSaveToExternalStoreAsync(projectorName, projectorVersion);

            // v10: Save to external store (Postgres/Cosmos) if available
            if (_multiProjectionStateStore != null && allowExternalStoreSave)
            {
                snapshotStream.Position = 0;
                var writeRequest = new MultiProjectionStateWriteRequest(
                    ProjectorName: projectorName,
                    ProjectorVersion: projectorVersion,
                    PayloadType: typeof(SerializableMultiProjectionStateEnvelope).FullName!,
                    LastSortableUniqueId: safePosition ?? string.Empty,
                    EventsProcessed: _eventsProcessed,
                    IsOffloaded: false,
                    OffloadKey: null,
                    OffloadProvider: null,
                    OriginalSizeBytes: originalSizeBytes,
                    CompressedSizeBytes: compressedSizeBytes,
                    SafeWindowThreshold: checkpoint.SafeThresholdValue ?? _host.PeekCurrentSafeWindowThreshold(),
                    CreatedAt: _state.State!.LastPersistTime == default
                        ? DateTime.UtcNow
                        : _state.State.LastPersistTime,
                    UpdatedAt: DateTime.UtcNow,
                    BuildSource: "GRAIN",
                    BuildHost: Environment.MachineName);

                var saveResult = await _multiProjectionStateStore.UpsertFromStreamAsync(
                    writeRequest,
                    snapshotStream,
                    _injectedActorOptions?.MaxSnapshotSerializedSizeBytes ?? 2 * 1024 * 1024,
                    CancellationToken.None);
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

            if (_multiProjectionStateStore != null && !allowExternalStoreSave)
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

            await WriteOrleansStateWithRetryAsync(projectorName, safePosition, projectorVersion, externalStoreSaved, safeVersion, envelopeSize);
            _host.CompactSafeHistory();
            CompactRetainedCollections();
            _lastError = null;
            var finishUtc = DateTime.UtcNow;
            _logger.LogDebug(
                "[{ProjectorName}] Persistence completed in {ElapsedMs:F0}ms - {EnvelopeSize:N0} bytes, {EventsProcessed:N0} events saved",
                projectorName,
                (finishUtc - startUtc).TotalMilliseconds,
                envelopeSize,
                _eventsProcessed);

            if (_forceGcAfterLargeSnapshotPersist && envelopeSize >= _largeSnapshotGcThresholdBytes)
            {
                TryCompactAfterLargePersist(projectorName, envelopeSize);
            }

            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _lastError = $"Persistence failed: {ex.Message}";
            _logger.LogError(ex, "[{ProjectorName}] Persistence failed", GetProjectorName());
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <summary>
    ///     Streaming persist path: writes snapshot to a temp file, then streams to external store.
    ///     Avoids holding the entire serialized snapshot in a byte[] simultaneously.
    /// </summary>
    private async Task<ResultBox<bool>> PersistStateStreamingAsync(string projectorName, PersistCheckpoint checkpoint)
    {
        var buildStartMs = System.Diagnostics.Stopwatch.GetTimestamp();
        string? tempFilePath = null;
        try
        {
            // Step 1: Write snapshot to temp file
            var (tempStream, filePath) = await _tempFileSnapshotManager!.CreateTempFileStreamAsync(projectorName);
            tempFilePath = filePath;

            try
            {
                var writeResult = await _host!.WriteSnapshotToStreamAsync(
                    tempStream, canGetUnsafeState: false, CancellationToken.None);
                if (!writeResult.IsSuccess)
                {
                    _lastError = writeResult.GetException().Message;
                    _logger.LogWarning("[{ProjectorName}] Streaming snapshot write failed: {Error}", projectorName, _lastError);
                    return ResultBox.FromValue(false);
                }

                await tempStream.FlushAsync();
                var tempFileSize = tempStream.Length;
                await tempStream.DisposeAsync();

                var buildElapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(buildStartMs).TotalMilliseconds;

                // Step 2: Collect metadata
                var safeVersion = checkpoint.SafeVersion;
                var projectorVersion = checkpoint.ProjectorVersion;
                var safePosition = checkpoint.SafePosition;

                // Step 3: Stream to external store
                var externalStorePersistResult = await SaveStreamingSnapshotToExternalStoreAsync(
                    projectorName,
                    checkpoint,
                    filePath,
                    tempFileSize);
                var externalStoreSaved = externalStorePersistResult.ExternalStoreSaved;

                var peakMemory = GC.GetTotalMemory(forceFullCollection: false);

                // Step 4: Update Orleans state
                _state.State!.ProjectorName = projectorName;
                _state.State.ProjectorVersion = projectorVersion;
                _state.State.LastSortableUniqueId = safePosition;
                _state.State.EventsProcessed = _eventsProcessed;
                _state.State.LastPersistTime = DateTime.UtcNow;

                if (externalStoreSaved)
                {
                    if (safeVersion.HasValue && safeVersion.Value > 0)
                        _state.State.LastGoodSafeVersion = safeVersion.Value;
                    if (tempFileSize > 0)
                        _state.State.LastGoodPayloadBytes = tempFileSize;
                    if (tempFileSize > 0)
                        _state.State.LastGoodOriginalSizeBytes = tempFileSize;
                    _state.State.LastGoodEventsProcessed = _eventsProcessed;
                }

                _state.State.SerializedState = null;
                _state.State.StateSize = 0;
                _state.State.SafeLastPosition = null;
                _state.State.LastPosition = null;

                await WriteOrleansStateWithRetryAsync(projectorName, safePosition, projectorVersion, externalStoreSaved, safeVersion, tempFileSize);
                _host.CompactSafeHistory();
                CompactRetainedCollections();

                _lastError = null;

                var metrics = new SnapshotPersistMetrics(
                    SnapshotBuildMs: (long)buildElapsedMs,
                    SnapshotUploadMs: externalStorePersistResult.UploadElapsedMs,
                    TempFileSizeBytes: tempFileSize,
                    PeakManagedMemoryBytes: peakMemory);
                _logger.LogInformation(
                    MultiProjectionLogEvents.PersistDetails,
                    "StreamingPersist: {ProjectorName}, BuildMs={BuildMs}, UploadMs={UploadMs}, TempFileBytes={TempFileBytes}, PeakMemory={PeakMemory}, Events={Events}",
                    projectorName, metrics.SnapshotBuildMs, metrics.SnapshotUploadMs,
                    metrics.TempFileSizeBytes, metrics.PeakManagedMemoryBytes, _eventsProcessed);

                if (_forceGcAfterLargeSnapshotPersist && tempFileSize >= _largeSnapshotGcThresholdBytes)
                {
                    TryCompactAfterLargePersist(projectorName, tempFileSize);
                }

                return ResultBox.FromValue(true);
            }
            catch
            {
                // Dispose stream on error path before delete
                try { tempStream.Dispose(); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Streaming persistence failed: {ex.Message}";
            _logger.LogError(ex, "[{ProjectorName}] Streaming persistence failed", projectorName);
            return ResultBox.Error<bool>(ex);
        }
        finally
        {
            if (tempFilePath is not null)
            {
                await _tempFileSnapshotManager!.SafeDeleteAsync(tempFilePath);
            }
        }
    }

    /// <summary>
    ///     Retry Orleans state write on ETag conflicts (optimistic concurrency).
    /// </summary>
    private async Task WriteOrleansStateWithRetryAsync(
        string projectorName,
        string? safePosition,
        string projectorVersion,
        bool externalStoreSaved,
        int? safeVersion,
        long envelopeSize)
    {
        const int maxRetries = 3;
        for (var retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                await _state.WriteStateAsync();
                break;
            }
            catch (global::Orleans.Storage.InconsistentStateException) when (retry < maxRetries - 1)
            {
                _logger.LogWarning(
                    "[{ProjectorName}] ETag conflict on Orleans state write (attempt {Attempt}/{MaxAttempts}), re-reading state...",
                    projectorName, retry + 1, maxRetries);
                await _state.ReadStateAsync();
                _state.State!.ProjectorName = projectorName;
                _state.State.ProjectorVersion = projectorVersion;
                _state.State.LastSortableUniqueId = safePosition;
                _state.State.EventsProcessed = _eventsProcessed;
                _state.State.LastPersistTime = DateTime.UtcNow;
                if (externalStoreSaved)
                {
                    if (safeVersion.HasValue && safeVersion.Value > 0)
                        _state.State.LastGoodSafeVersion = safeVersion.Value;
                    if (envelopeSize > 0)
                        _state.State.LastGoodPayloadBytes = envelopeSize;
                    if (envelopeSize > 0)
                        _state.State.LastGoodOriginalSizeBytes = envelopeSize;
                    _state.State.LastGoodEventsProcessed = _eventsProcessed;
                }
                _state.State.SerializedState = null;
                _state.State.StateSize = 0;
                _state.State.SafeLastPosition = null;
                _state.State.LastPosition = null;
                await Task.Delay(50 * (retry + 1));
            }
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

    public Task<SerializableQueryResult> ExecuteQueryAsync(SerializableQueryParameter queryParameter) =>
        ExecuteQueryInternalAsync(queryParameter, waitForCatchUp: false);

    public Task<SerializableQueryResult> ExecuteQueryAsync(SerializableQueryParameter queryParameter, bool waitForCatchUp) =>
        ExecuteQueryInternalAsync(queryParameter, waitForCatchUp);

    public Task<SerializableListQueryResult> ExecuteListQueryAsync(SerializableQueryParameter queryParameter) =>
        ExecuteListQueryInternalAsync(queryParameter, waitForCatchUp: false);

    public Task<SerializableListQueryResult> ExecuteListQueryAsync(SerializableQueryParameter queryParameter, bool waitForCatchUp) =>
        ExecuteListQueryInternalAsync(queryParameter, waitForCatchUp);

    private sealed record QueryExecutionMetadata(
        int? SafeVersion,
        string? SafeThreshold,
        DateTime? SafeThresholdTime,
        int? UnsafeVersion,
        bool IsCatchUpInProgress);

    private async Task<SerializableQueryResult> ExecuteQueryInternalAsync(
        SerializableQueryParameter queryParameter, bool waitForCatchUp)
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
            var queryMetadata = await GetQueryExecutionMetadataAsync(waitForCatchUp);

            var result = await _host.ExecuteQueryAsync(
                queryParameter,
                queryMetadata.SafeVersion,
                queryMetadata.SafeThreshold,
                queryMetadata.SafeThresholdTime,
                queryMetadata.UnsafeVersion);

            if (!result.IsSuccess)
            {
                throw result.GetException();
            }

            var resultValue = result.GetValue();

            if (queryMetadata.IsCatchUpInProgress)
            {
                resultValue = resultValue with { IsCatchUpInProgress = true };
            }

            return resultValue;
        }
        catch (Exception ex)
        {
            _lastError = $"Query failed: {ex.Message}";
            throw;
        }
    }

    private async Task<SerializableListQueryResult> ExecuteListQueryInternalAsync(
        SerializableQueryParameter queryParameter, bool waitForCatchUp)
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
            var queryMetadata = await GetQueryExecutionMetadataAsync(waitForCatchUp);

            var result = await _host.ExecuteListQueryAsync(
                queryParameter,
                queryMetadata.SafeVersion,
                queryMetadata.SafeThreshold,
                queryMetadata.SafeThresholdTime,
                queryMetadata.UnsafeVersion);

            if (!result.IsSuccess)
            {
                throw result.GetException();
            }

            var resultValue = result.GetValue();

            if (queryMetadata.IsCatchUpInProgress)
            {
                resultValue = resultValue with { IsCatchUpInProgress = true };
            }

            return resultValue;
        }
        catch (Exception ex)
        {
            _lastError = $"List query failed: {ex.Message}";
            throw;
        }
    }

    private async Task<QueryExecutionMetadata> GetQueryExecutionMetadataAsync(bool waitForCatchUp)
    {
        var isCatchUpInProgress = await PrepareForQueryExecutionAsync(waitForCatchUp);

        int? safeVersion = null;
        string? safeThreshold = null;
        DateTime? safeThresholdTime = null;
        int? unsafeVersion = null;

        var safeStateResult = await _host!.GetStateAsync(canGetUnsafeState: false);
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

        return new QueryExecutionMetadata(
            safeVersion,
            safeThreshold,
            safeThresholdTime,
            unsafeVersion,
            isCatchUpInProgress);
    }

    private async Task<bool> PrepareForQueryExecutionAsync(bool waitForCatchUp)
    {
        await StartSubscriptionAsync();

        if (_orleansStreamHandle == null || waitForCatchUp)
        {
            await CatchUpFromEventStoreAsync();
        }

        var isCatchUpInProgress = _catchUpProgress.IsActive;
        if (waitForCatchUp && isCatchUpInProgress)
        {
            await WaitForCatchUpWithTimeoutAsync(TimeSpan.FromSeconds(30));
            return _catchUpProgress.IsActive;
        }

        return isCatchUpInProgress;
    }

    public async Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        await EnsureInitializedAsync();

        if (_host == null) return false;

        return await _host.IsSortableUniqueIdReceivedAsync(sortableUniqueId);
    }

    public async Task RefreshAsync()
    {
        var projectorName = GetProjectorName();
        _logger.LogDebug("[{ProjectorName}] Refreshing: Re-reading events from event store", projectorName);

        await EnsureInitializedAsync();
        if (_host == null)
        {
            return;
        }

        RecoverStaleCatchUpIfNeeded(projectorName);
        if (_catchUpProgress.IsActive)
        {
            _logger.LogDebug(
                "[{ProjectorName}] Refresh skipped because catch-up is already active",
                projectorName);
            return;
        }

        // Refresh is expected to complete catch-up before returning.
        // Use an in-call batch loop instead of timer-driven background catch-up.
        _catchUpTimer?.Dispose();
        _catchUpTimer = null;

        var currentPosition = await GetCurrentPositionAsync();
        _catchUpProgress = new CatchUpProgress
        {
            CurrentPosition = currentPosition,
            TargetPosition = null,
            IsActive = true,
            HadNewEvents = false,
            ConsecutiveEmptyBatches = 0,
            BatchesProcessed = 0,
            StartTime = DateTime.UtcNow,
            LastAttempt = DateTime.MinValue
        };
        ResetHybridCatchUpLogging();
        ResetCatchUpFailureTracking();

        MoveBufferedStreamEventsToPending(currentPosition);

        const int maxRefreshBatches = 20000;
        for (var i = 0; i < maxRefreshBatches && _catchUpProgress.IsActive; i++)
        {
            var processed = await ProcessSingleCatchUpBatch();
            if (processed == 0)
            {
                _catchUpProgress.ConsecutiveEmptyBatches++;
                if (_catchUpProgress.ConsecutiveEmptyBatches >= MaxConsecutiveEmptyBatches)
                {
                    await CompleteCatchUp();
                }
            }
            else
            {
                _catchUpProgress.ConsecutiveEmptyBatches = 0;
            }
        }
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
            var baseOptions = _injectedActorOptions ?? DefaultActorOptions;
            var persistPolicySettings = ResolvePersistPolicySettings(projectorName);
            var mergedOptions = new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = baseOptions.SafeWindowMs,
                MaxSnapshotSerializedSizeBytes = baseOptions.MaxSnapshotSerializedSizeBytes,
                MaxPendingStreamEvents = baseOptions.MaxPendingStreamEvents,
                CatchUpBatchSize = baseOptions.CatchUpBatchSize,
                CatchUpDeactivationDelayMinutes = baseOptions.CatchUpDeactivationDelayMinutes,
                CatchUpMaxConsecutiveFailures = baseOptions.CatchUpMaxConsecutiveFailures,
                CatchUpMaxFailureDurationSeconds = baseOptions.CatchUpMaxFailureDurationSeconds,
                PersistBatchSize = persistPolicySettings.PersistBatchSize,
                PersistIntervalSeconds = persistPolicySettings.PersistInterval > TimeSpan.Zero
                    ? (int)persistPolicySettings.PersistInterval.TotalSeconds
                    : 0,
                SkipPersistWhenSafeCheckpointUnchanged = persistPolicySettings.SkipPersistWhenSafeCheckpointUnchanged,
                EnableDynamicSafeWindow = baseOptions.EnableDynamicSafeWindow,
                MaxExtraSafeWindowMs = baseOptions.MaxExtraSafeWindowMs,
                LagEmaAlpha = baseOptions.LagEmaAlpha,
                LagDecayPerSecond = baseOptions.LagDecayPerSecond,
                FailOnUnhealthyActivation = baseOptions.FailOnUnhealthyActivation,
                ProcessedEventIdCacheSize = baseOptions.ProcessedEventIdCacheSize,
                ForceGcAfterLargeSnapshotPersist = baseOptions.ForceGcAfterLargeSnapshotPersist,
                LargeSnapshotGcThresholdBytes = baseOptions.LargeSnapshotGcThresholdBytes,
                UseStreamingSnapshotIO = baseOptions.UseStreamingSnapshotIO,
                ProjectorPersistenceOverrides = baseOptions.ProjectorPersistenceOverrides
            };
            _persistBatchSize = mergedOptions.PersistBatchSize;
            _persistInterval = mergedOptions.PersistIntervalSeconds > 0
                ? TimeSpan.FromSeconds(mergedOptions.PersistIntervalSeconds)
                : TimeSpan.Zero;
            _skipPersistWhenSafeCheckpointUnchanged = mergedOptions.SkipPersistWhenSafeCheckpointUnchanged;
            _maxPendingStreamEvents = mergedOptions.MaxPendingStreamEvents;
            _catchUpBatchSize = Math.Max(1, mergedOptions.CatchUpBatchSize);
            _catchUpDeactivationDelay = TimeSpan.FromMinutes(Math.Max(1, mergedOptions.CatchUpDeactivationDelayMinutes));
            _catchUpMaxConsecutiveFailures = Math.Max(1, mergedOptions.CatchUpMaxConsecutiveFailures);
            _catchUpMaxFailureDuration = TimeSpan.FromSeconds(Math.Max(10, mergedOptions.CatchUpMaxFailureDurationSeconds));
            _processedEventIdCacheSize = Math.Max(1000, mergedOptions.ProcessedEventIdCacheSize);
            _forceGcAfterLargeSnapshotPersist = mergedOptions.ForceGcAfterLargeSnapshotPersist;
            _largeSnapshotGcThresholdBytes = Math.Max(1_000_000, mergedOptions.LargeSnapshotGcThresholdBytes);
            _useStreamingSnapshotIO = mergedOptions.UseStreamingSnapshotIO;

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
                        var stateStreamResult = await _multiProjectionStateStore.OpenStateDataReadStreamAsync(
                            record,
                            cancellationToken);

                        if (!stateStreamResult.IsSuccess)
                        {
                            var errorMsg = record.IsOffloaded
                                ? $"State stream open failed for offloaded key: {record.OffloadKey}"
                                : stateStreamResult.GetException().Message;
                            _logger.LogError(
                                MultiProjectionLogEvents.BlobReadFailed,
                                stateStreamResult.GetException(),
                                "State stream open failed: {ProjectorName}, IsOffloaded: {IsOffloaded}, OffloadKey: {OffloadKey}",
                                projectorName,
                                record.IsOffloaded,
                                record.OffloadKey);
                            _stateRestoreSource = StateRestoreSource.Failed;
                            _activationFailureReason = errorMsg;
                            forceFullCatchUp = true;
                        }
                        else
                        {
                            await using var snapshotStream = stateStreamResult.GetValue();
                            long? restoredSnapshotBytes = null;
                            if (snapshotStream.CanSeek)
                            {
                                restoredSnapshotBytes = snapshotStream.Length;
                                snapshotStream.Position = 0;
                            }
                            var restoreResult = await _host.RestoreSnapshotFromStreamAsync(snapshotStream, cancellationToken);

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
                                ClearProcessedEventCache();

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
                                    restoredSnapshotBytes ?? 0,
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

        // Cleanup stale temp files from previous activations
        if (_tempFileSnapshotManager is not null)
        {
            await _tempFileSnapshotManager.CleanupStaleFilesAsync();
        }

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
        ResetCatchUpFailureTracking();
        EndCatchUpDeactivationDelay();

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
        ClearProcessedEventCache();
        _unsafeEventIds.Clear();
        _eventBuffer.Clear();
        _pendingStreamEvents.Clear();
        CompactRetainedCollections();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        _isInitialized = true;

        ApplyPersistPolicySettings(GetProjectorName());

        // Set up periodic persistence timer
        if (_persistInterval > TimeSpan.Zero)
        {
            _persistTimer = this.RegisterGrainTimer(
                async () => await PersistStateAsync(),
                new GrainTimerCreationOptions
                {
                    DueTime = _persistInterval,
                    Period = _persistInterval,
                    Interleave = true
                });
        }

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
        EndCatchUpDeactivationDelay();
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private void BeginCatchUpDeactivationDelay()
    {
        DelayDeactivation(_catchUpDeactivationDelay);
        _catchUpDeactivationDelayActive = true;
    }

    private void RenewCatchUpDeactivationDelay()
    {
        if (!_catchUpDeactivationDelayActive)
        {
            return;
        }
        DelayDeactivation(_catchUpDeactivationDelay);
    }

    private void EndCatchUpDeactivationDelay()
    {
        if (!_catchUpDeactivationDelayActive)
        {
            return;
        }
        DelayDeactivation(TimeSpan.Zero);
        _catchUpDeactivationDelayActive = false;
    }

    private void ResetCatchUpFailureTracking()
    {
        _catchUpConsecutiveFailureCount = 0;
        _catchUpFailureWindowStartUtc = null;
    }

    private void HandleCatchUpBatchFailure(Exception ex, string projectorName)
    {
        _catchUpConsecutiveFailureCount++;
        _catchUpFailureWindowStartUtc ??= DateTime.UtcNow;

        var failureElapsed = DateTime.UtcNow - _catchUpFailureWindowStartUtc.Value;
        var shouldAbort =
            _catchUpConsecutiveFailureCount >= _catchUpMaxConsecutiveFailures ||
            failureElapsed >= _catchUpMaxFailureDuration;

        _lastError = $"Catch-up batch failed: {ex.Message}";
        _logger.LogError(
            ex,
            "[{ProjectorName}] Catch-up batch error (consecutive failures: {FailureCount}, elapsed: {FailureElapsedSeconds:F1}s)",
            projectorName,
            _catchUpConsecutiveFailureCount,
            failureElapsed.TotalSeconds);

        if (!shouldAbort)
        {
            return;
        }

        _catchUpProgress.IsActive = false;
        _catchUpTimer?.Dispose();
        _catchUpTimer = null;
        EndCatchUpDeactivationDelay();

        _logger.LogWarning(
            "[{ProjectorName}] Catch-up stopped after repeated failures (consecutive failures: {FailureCount}, elapsed: {FailureElapsedSeconds:F1}s)",
            projectorName,
            _catchUpConsecutiveFailureCount,
            failureElapsed.TotalSeconds);
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

                    var stateStreamResult = await _multiProjectionStateStore.OpenStateDataReadStreamAsync(
                        record,
                        CancellationToken.None);
                    if (!stateStreamResult.IsSuccess)
                    {
                        throw stateStreamResult.GetException();
                    }

                    await using var sourceStream = stateStreamResult.GetValue();
                    await using var targetStream = new MemoryStream();
                    var rewriteResult = await _host.RewriteSnapshotVersionAsync(
                        sourceStream,
                        targetStream,
                        newVersion,
                        CancellationToken.None);
                    if (!rewriteResult.IsSuccess)
                    {
                        throw rewriteResult.GetException();
                    }

                    targetStream.Position = 0;
                    var writeRequest = new MultiProjectionStateWriteRequest(
                        ProjectorName: record.ProjectorName,
                        ProjectorVersion: newVersion,
                        PayloadType: typeof(SerializableMultiProjectionStateEnvelope).FullName!,
                        LastSortableUniqueId: record.LastSortableUniqueId,
                        EventsProcessed: record.EventsProcessed,
                        IsOffloaded: false,
                        OffloadKey: null,
                        OffloadProvider: null,
                        OriginalSizeBytes: targetStream.Length,
                        CompressedSizeBytes: targetStream.Length,
                        SafeWindowThreshold: record.SafeWindowThreshold,
                        CreatedAt: record.CreatedAt,
                        UpdatedAt: DateTime.UtcNow,
                        BuildSource: record.BuildSource,
                        BuildHost: record.BuildHost);

                    var saveResult = await _multiProjectionStateStore.UpsertFromStreamAsync(
                        writeRequest,
                        targetStream,
                        _injectedActorOptions?.MaxSnapshotSerializedSizeBytes ?? 2 * 1024 * 1024,
                        CancellationToken.None);
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

    public async Task SeedEventsAsync(IReadOnlyList<SerializableEvent> events)
    {
        if (_eventStore == null) return;
        var result = await _eventStore.WriteSerializableEventsAsync(events);
        if (!result.IsSuccess)
        {
            throw result.GetException();
        }
    }

    private async Task FallbackEventCheckAsync()
    {
        if (_catchUpProgress.IsActive)
        {
            _logger.LogDebug(
                "[{ProjectorName}] Fallback check skipped because catch-up is already active",
                GetProjectorName());
            return;
        }

        // Only run fallback if we haven't received events recently
        if (_lastEventTime == null || DateTime.UtcNow - _lastEventTime > TimeSpan.FromMinutes(1))
        {
            _logger.LogDebug(
                "[{ProjectorName}] Fallback: No stream events for over 1 minute, checking event store",
                GetProjectorName());
            await RefreshAsync();
        }
    }

    private async Task CatchUpFromEventStoreAsync(bool forceFull = false)
    {
        // Legacy method for compatibility - now triggers timer-based catch-up
        if (_host == null || _eventStore == null) return;

        RecoverStaleCatchUpIfNeeded(GetProjectorName());

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

        RecoverStaleCatchUpIfNeeded(projectorName);

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
            BeginCatchUpDeactivationDelay();

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
                HadNewEvents = false,
                ConsecutiveEmptyBatches = 0,
                BatchesProcessed = 0,
                StartTime = DateTime.UtcNow,
                LastAttempt = DateTime.MinValue
            };
            ResetHybridCatchUpLogging();
            ResetCatchUpFailureTracking();
            _eventsProcessedSinceLastCatchUpPersist = 0;
            _lastCatchUpPersistUtc = DateTime.UtcNow;

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
            EndCatchUpDeactivationDelay();
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
            EndCatchUpDeactivationDelay();
            return;
        }

        RenewCatchUpDeactivationDelay();

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
            ResetCatchUpFailureTracking();

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
                // BatchesProcessed is now incremented inside UpdateCatchUpProgressAfterBatch
                // so that progress logging within that method sees the correct batch number.
            }
        }
        catch (Exception ex)
        {
            HandleCatchUpBatchFailure(ex, projectorName);
        }
        finally
        {
            CatchUpBatchSemaphore.Release();
        }
    }

    private async Task<int> ProcessSingleCatchUpBatch()
    {
        if (_host == null) return 0;
        _catchUpProgress.LastAttempt = DateTime.UtcNow;
        return await ProcessSerializableBatch();
    }

    private void RecoverStaleCatchUpIfNeeded(string projectorName)
    {
        if (!_catchUpProgress.IsActive)
        {
            return;
        }

        var lastProgressAt =
            _catchUpProgress.LastAttempt == DateTime.MinValue
                ? _catchUpProgress.StartTime
                : _catchUpProgress.LastAttempt;
        var hasProgressTimestamp = lastProgressAt != DateTime.MinValue;
        var isStalled =
            _catchUpTimer == null ||
            (hasProgressTimestamp && DateTime.UtcNow - lastProgressAt > CatchUpStallThreshold);

        if (!isStalled)
        {
            return;
        }

        _logger.LogWarning(
            "[{ProjectorName}] Recovering stale catch-up state. LastAttempt={LastAttempt}, StartTime={StartTime}, TimerPresent={HasTimer}",
            projectorName,
            _catchUpProgress.LastAttempt,
            _catchUpProgress.StartTime,
            _catchUpTimer is not null);

        _catchUpProgress.IsActive = false;
        _catchUpTimer?.Dispose();
        _catchUpTimer = null;
        ResetCatchUpFailureTracking();
        EndCatchUpDeactivationDelay();
    }

    /// <summary>
    ///     Catch-up via ReadAllSerializableEventsAsync (cold/hot merge path).
    /// </summary>
    private async Task<int> ProcessSerializableBatch()
    {
        var projectorName = GetProjectorName();
        var catchUpStore = GetCatchUpEventStore();
        var hybridCatchUpStore = catchUpStore as HybridEventStore;
        var isHybridCatchUp = hybridCatchUpStore is not null;
        var batchSize = ResolveCatchUpBatchSize(hybridCatchUpStore);
        var startPosition = _catchUpProgress.CurrentPosition?.Value ?? "beginning";

        if (isHybridCatchUp && !_hybridCatchUpCheckLogged)
        {
            _logger.LogInformation(
                "[{ProjectorName}] Catch-up is checking cold storage via hybrid event store (ServiceId={ServiceId}, StartPosition={StartPosition}, RequestedMaxEvents={RequestedMaxEvents})",
                projectorName,
                _serviceId,
                startPosition,
                batchSize);
            _hybridCatchUpCheckLogged = true;
        }

        if (catchUpStore is IStreamingSerializableEventStore streamingCatchUpStore)
        {
            return await ProcessStreamingSerializableBatch(
                streamingCatchUpStore,
                hybridCatchUpStore,
                isHybridCatchUp,
                batchSize,
                startPosition,
                projectorName);
        }

        ResultBox<IEnumerable<SerializableEvent>> eventsResult;
        HybridReadBatchMetadata? hybridReadBatchMetadata = null;
        try
        {
            using var projectionContext = HybridReadProjectionContext.Push(projectorName);
            eventsResult = await catchUpStore.ReadAllSerializableEventsAsync(
                _catchUpProgress.CurrentPosition,
                batchSize);
            hybridReadBatchMetadata = HybridReadProjectionContext.BatchMetadata;
        }
        catch (NotSupportedException)
        {
            throw new InvalidOperationException(
                $"[{projectorName}] Serializable catch-up is required but the configured event store does not support ReadAllSerializableEventsAsync.");
        }

        if (!eventsResult.IsSuccess)
        {
            var exception = eventsResult.GetException();
            if (exception is NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"[{projectorName}] Serializable catch-up is required but the configured event store returned NotSupported for ReadAllSerializableEventsAsync.",
                    exception);
            }

            _logger.LogError(
                exception,
                "[{ProjectorName}] Failed to read serializable events for catch-up",
                projectorName);
            return 0;
        }

        _logger.LogDebug(
            "[{ProjectorName}] Catch-up using serializable path (cold/hot merge)",
            projectorName);

        var eventsEnumerable = eventsResult.GetValue();
        var events = eventsEnumerable as IReadOnlyList<SerializableEvent> ?? eventsEnumerable.ToList();
        if (isHybridCatchUp && !_hybridCatchUpFirstBatchLogged)
        {
            _logger.LogInformation(
                "[{ProjectorName}] Catch-up hybrid read fetched {FetchedEvents} events (RequestedMaxEvents={RequestedMaxEvents}, StartPosition={StartPosition})",
                projectorName,
                events.Count,
                batchSize,
                startPosition);
            _hybridCatchUpFirstBatchLogged = true;
        }
        if (events.Count == 0)
            return 0;

        UpdateTargetPosition(events[^1].SortableUniqueIdValue);

        var filtered = FilterByPositionAndProcessed(events, e => e.Id, e => e.SortableUniqueIdValue);
        if (filtered.Count == 0)
        {
            _logger.LogInformation(
                "[{ProjectorName}] Catch-up batch read {FetchedCount} events but filtered all of them. CurrentPosition={CurrentPosition}, LastFetched={LastFetched}",
                projectorName,
                events.Count,
                _catchUpProgress.CurrentPosition?.Value ?? "beginning",
                events[^1].SortableUniqueIdValue);
            _catchUpProgress.CurrentPosition = new SortableUniqueId(events[^1].SortableUniqueIdValue);
            return 0;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "[{ProjectorName}] Catch-up batch applying {EventCount} events. First={FirstSortableId}/{FirstType}, Last={LastSortableId}/{LastType}, StartPosition={StartPosition}",
            projectorName,
            filtered.Count,
            filtered[0].SortableUniqueIdValue,
            filtered[0].EventPayloadName,
            filtered[^1].SortableUniqueIdValue,
            filtered[^1].EventPayloadName,
            startPosition);
        try
        {
            await _host!.AddSerializableEventsAsync(filtered, finishedCatchUp: false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Unknown event type", StringComparison.Ordinal))
        {
            _logger.LogError(
                ex,
                "[{ProjectorName}] Serializable catch-up failed due to unknown event type",
                projectorName);
            throw;
        }
        sw.Stop();
        _logger.LogInformation(
            "[{ProjectorName}] Catch-up batch apply completed in {ElapsedMs} ms. LastApplied={LastSortableId}",
            projectorName,
            sw.ElapsedMilliseconds,
            filtered[^1].SortableUniqueIdValue);

        await UpdateCatchUpProgressAfterBatch(
            filtered.Select(e => e.Id),
            filtered[^1].SortableUniqueIdValue,
            filtered.Count,
            sw.ElapsedMilliseconds,
            hybridCatchUpStore,
            hybridReadBatchMetadata);

        return filtered.Count;
    }

    private async Task<int> ProcessStreamingSerializableBatch(
        IStreamingSerializableEventStore streamingCatchUpStore,
        HybridEventStore? hybridCatchUpStore,
        bool isHybridCatchUp,
        int batchSize,
        string startPosition,
        string projectorName)
    {
        if (_host == null)
        {
            return 0;
        }

        var processedIds = new List<Guid>(Math.Min(batchSize, StreamingCatchUpApplyChunkSize * 2));
        var buffer = new List<SerializableEvent>(Math.Min(batchSize, StreamingCatchUpApplyChunkSize));
        string? lastFetchedSortableUniqueId = null;
        string? lastProcessedSortableUniqueId = null;
        var fetchedCount = 0;
        var filteredCount = 0;
        long applyElapsedMs = 0;
        HybridReadBatchMetadata? hybridReadBatchMetadata = null;

        async Task FlushBufferAsync()
        {
            if (buffer.Count == 0)
            {
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _host.AddSerializableEventsAsync(buffer, finishedCatchUp: false);
            sw.Stop();
            applyElapsedMs += sw.ElapsedMilliseconds;

            foreach (var item in buffer)
            {
                processedIds.Add(item.Id);
            }

            filteredCount += buffer.Count;
            lastProcessedSortableUniqueId = buffer[^1].SortableUniqueIdValue;
            buffer.Clear();
        }

        try
        {
            using var projectionContext = HybridReadProjectionContext.Push(projectorName);
            var streamResult = await streamingCatchUpStore.StreamAllSerializableEventsAsync(
                _catchUpProgress.CurrentPosition,
                batchSize,
                async ev =>
                {
                    fetchedCount++;
                    lastFetchedSortableUniqueId = ev.SortableUniqueIdValue;
                    UpdateTargetPosition(ev.SortableUniqueIdValue);

                    if (_processedEventIds.Contains(ev.Id))
                    {
                        return;
                    }

                    if (_catchUpProgress.CurrentPosition != null &&
                        string.Compare(ev.SortableUniqueIdValue, _catchUpProgress.CurrentPosition.Value, StringComparison.Ordinal) <= 0)
                    {
                        return;
                    }

                    buffer.Add(ev);
                    if (buffer.Count >= StreamingCatchUpApplyChunkSize)
                    {
                        await FlushBufferAsync();
                    }
                });
            hybridReadBatchMetadata = HybridReadProjectionContext.BatchMetadata;

            if (!streamResult.IsSuccess)
            {
                var exception = streamResult.GetException();
                _logger.LogError(
                    exception,
                    "[{ProjectorName}] Failed to stream serializable events for catch-up",
                    projectorName);
                return 0;
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Unknown event type", StringComparison.Ordinal))
        {
            _logger.LogError(
                ex,
                "[{ProjectorName}] Serializable catch-up failed due to unknown event type",
                projectorName);
            throw;
        }

        if (isHybridCatchUp && !_hybridCatchUpFirstBatchLogged)
        {
            _logger.LogInformation(
                "[{ProjectorName}] Catch-up hybrid read fetched {FetchedEvents} events (RequestedMaxEvents={RequestedMaxEvents}, StartPosition={StartPosition})",
                projectorName,
                fetchedCount,
                batchSize,
                startPosition);
            _hybridCatchUpFirstBatchLogged = true;
        }

        if (fetchedCount == 0)
        {
            return 0;
        }

        await FlushBufferAsync();
        if (filteredCount == 0)
        {
            _catchUpProgress.CurrentPosition = new SortableUniqueId(lastFetchedSortableUniqueId!);
            return 0;
        }

        await UpdateCatchUpProgressAfterBatch(
            processedIds,
            lastProcessedSortableUniqueId!,
            filteredCount,
            applyElapsedMs,
            hybridCatchUpStore,
            hybridReadBatchMetadata);

        return filteredCount;
    }

    private int ResolveCatchUpBatchSize(HybridEventStore? hybridCatchUpStore)
    {
        if (hybridCatchUpStore is null)
        {
            return _catchUpBatchSize;
        }

        if (_lastHybridReadBatchMetadata?.UsedCold == true)
        {
            return Math.Max(_catchUpBatchSize, hybridCatchUpStore.GetPreferredCatchUpBatchSize());
        }

        return _catchUpBatchSize;
    }

    private IReadOnlyList<T> FilterByPositionAndProcessed<T>(
        IReadOnlyList<T> events,
        Func<T, Guid> idSelector,
        Func<T, string> sortableIdSelector)
    {
        List<T>? filtered = null;
        for (var index = 0; index < events.Count; index++)
        {
            var ev = events[index];
            if (_processedEventIds.Contains(idSelector(ev)))
            {
                if (filtered == null)
                {
                    filtered = new List<T>(events.Count);
                    for (var copyIndex = 0; copyIndex < index; copyIndex++)
                    {
                        filtered.Add(events[copyIndex]);
                    }
                }
                continue;
            }
            if (_catchUpProgress.CurrentPosition != null &&
                string.Compare(sortableIdSelector(ev), _catchUpProgress.CurrentPosition.Value, StringComparison.Ordinal) <= 0)
            {
                if (filtered == null)
                {
                    filtered = new List<T>(events.Count);
                    for (var copyIndex = 0; copyIndex < index; copyIndex++)
                    {
                        filtered.Add(events[copyIndex]);
                    }
                }
                continue;
            }
            filtered?.Add(ev);
        }
        return filtered ?? events;
    }

    private void UpdateTargetPosition(string latestSortableUniqueIdValue)
    {
        if (_catchUpProgress.TargetPosition == null ||
            string.Compare(latestSortableUniqueIdValue, _catchUpProgress.TargetPosition.Value, StringComparison.Ordinal) > 0)
        {
            _catchUpProgress.TargetPosition = new SortableUniqueId(latestSortableUniqueIdValue);
        }
    }

    private async Task UpdateCatchUpProgressAfterBatch(
        IEnumerable<Guid> processedIds,
        string lastSortableUniqueIdValue,
        int filteredCount,
        long elapsedMs,
        HybridEventStore? hybridCatchUpStore,
        HybridReadBatchMetadata? hybridReadBatchMetadata)
    {
        var projectorName = GetProjectorName();

        _catchUpProgress.BatchesProcessed++;
        _catchUpProgress.HadNewEvents = true;
        _eventsProcessed += filteredCount;
        _eventsProcessedSinceLastCatchUpPersist += filteredCount;
        _lastHybridReadBatchMetadata = hybridReadBatchMetadata;

        if (elapsedMs > 1000)
        {
            _logger.LogWarning(
                "[{ProjectorName}] Catch-up batch took {ElapsedMs}ms for {EventCount} events",
                projectorName,
                elapsedMs,
                filteredCount);
        }

        foreach (var id in processedIds)
        {
            TrackProcessedEventId(id);
        }

        _catchUpProgress.CurrentPosition = new SortableUniqueId(lastSortableUniqueIdValue);

        var shouldPersist = ShouldPersistAfterCatchUpBatch(hybridCatchUpStore, hybridReadBatchMetadata);
        if (shouldPersist)
        {
            _logger.LogDebug(
                "[{ProjectorName}] Persisting state at {EventsProcessed:N0} events (UsedCold={UsedCold}, ReachedColdSegmentBoundary={ReachedColdSegmentBoundary})",
                projectorName,
                _eventsProcessed,
                hybridReadBatchMetadata?.UsedCold ?? false,
                hybridReadBatchMetadata?.ReachedColdSegmentBoundary ?? false);
            await PersistStateAsync();
            _eventsProcessedSinceLastCatchUpPersist = 0;
            _lastCatchUpPersistUtc = DateTime.UtcNow;
        }

        if (_catchUpProgress.BatchesProcessed % 10 == 0)
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
    }

    private bool ShouldPersistAfterCatchUpBatch(
        HybridEventStore? hybridCatchUpStore,
        HybridReadBatchMetadata? hybridReadBatchMetadata)
    {
        if (hybridCatchUpStore is not null && hybridReadBatchMetadata?.UsedCold == true)
        {
            if (hybridCatchUpStore.ShouldPersistSnapshotOnColdSegmentBoundary()
                && hybridReadBatchMetadata.ReachedColdSegmentBoundary)
            {
                return true;
            }

            if (_eventsProcessedSinceLastCatchUpPersist >= hybridCatchUpStore.GetCatchUpPersistMaxEventsWithoutSnapshot())
            {
                return true;
            }

            if (DateTime.UtcNow - _lastCatchUpPersistUtc >= hybridCatchUpStore.GetCatchUpPersistMaxInterval())
            {
                return true;
            }

            return false;
        }

        return _eventsProcessed > 0 && _eventsProcessed % 5000 == 0;
    }

    private async Task CompleteCatchUp()
    {
        var projectorName = GetProjectorName();
        var shouldPersist = _catchUpProgress.HadNewEvents;

        try
        {
            // Stop timer
            _catchUpTimer?.Dispose();
            _catchUpTimer = null;

            // Process all buffered events first
            await FlushEventBufferAsync();

            if (shouldPersist)
            {
                // Force promotion of any events that are now safe
                await TriggerSafePromotion();

                // Final persistence
                await PersistStateAsync();
            }

            // Process any pending stream events
            await ProcessPendingStreamEvents();
            CompactRetainedCollections();

            _catchUpProgress.IsActive = false;

            var elapsed = DateTime.UtcNow - _catchUpProgress.StartTime;
            _logger.LogDebug(
                "[{ProjectorName}] Catch-up completed: {BatchCount} batches, {EventsProcessed:N0} events, elapsed: {ElapsedSeconds:F1}s",
                projectorName,
                _catchUpProgress.BatchesProcessed,
                _eventsProcessed,
                elapsed.TotalSeconds);
        }
        finally
        {
            _catchUpProgress.IsActive = false;
            ResetCatchUpFailureTracking();
            EndCatchUpDeactivationDelay();
        }
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
            if (_processedEventIds.Contains(ev.Id))
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
                TrackProcessedEventId(ev.Id);
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

        // Prefer the full state when available, but fall back to primitive metadata.
        // Catch-up progress tracking should not depend on payload deserialization succeeding.
        var currentState = await _host.GetStateAsync(canGetUnsafeState: false);
        if (currentState.IsSuccess)
        {
            var state = currentState.GetValue();
            if (!string.IsNullOrEmpty(state.LastSortableUniqueId))
            {
                return new SortableUniqueId(state.LastSortableUniqueId);
            }
        }

        var metadata = await _host.GetStateMetadataAsync(includeUnsafe: true);
        if (metadata.IsSuccess)
        {
            var value = metadata.GetValue();
            if (!string.IsNullOrWhiteSpace(value.UnsafeLastSortableUniqueId))
            {
                return new SortableUniqueId(value.UnsafeLastSortableUniqueId);
            }

            if (!string.IsNullOrWhiteSpace(value.SafeLastSortableUniqueId))
            {
                return new SortableUniqueId(value.SafeLastSortableUniqueId);
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

    private void TrackProcessedEventId(Guid eventId)
    {
        if (!_processedEventIds.Add(eventId))
        {
            return;
        }

        _processedEventIdOrder.Enqueue(eventId);
        TrimProcessedEventCacheIfNeeded();
    }

    private void TrimProcessedEventCacheIfNeeded()
    {
        while (_processedEventIdCacheSize > 0 && _processedEventIdOrder.Count > _processedEventIdCacheSize)
        {
            var oldest = _processedEventIdOrder.Dequeue();
            _processedEventIds.Remove(oldest);
        }
    }

    private void ClearProcessedEventCache()
    {
        _processedEventIds.Clear();
        _processedEventIds.TrimExcess();
        _processedEventIdOrder.Clear();
        _processedEventIdOrder.TrimExcess();
    }

    private void CompactRetainedCollections()
    {
        _processedEventIds.TrimExcess();
        _processedEventIdOrder.TrimExcess();

        lock (_eventBuffer)
        {
            _eventBuffer.TrimExcess();
        }

        _unsafeEventIds.TrimExcess();
        _pendingStreamEvents.TrimExcess();
    }

    private void TryCompactAfterLargePersist(string projectorName, long persistedBytes)
    {
        try
        {
            _logger.LogInformation(
                "[{ProjectorName}] Triggering post-persist GC compaction for large snapshot ({PersistedBytes:N0} bytes)",
                projectorName,
                persistedBytes);
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "[{ProjectorName}] Post-persist GC compaction failed",
                projectorName);
        }
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
                while (_maxPendingStreamEvents > 0 && _pendingStreamEvents.Count > _maxPendingStreamEvents)
                {
                    _pendingStreamEvents.Dequeue();
                }

                return;
            }

            // Normal processing mode - filter and process
            var newEvents = events.Where(e => !_processedEventIds.Contains(e.Id)).ToList();

            if (newEvents.Count > 0)
            {
                // Delegate to host - host handles safe/unsafe internally
                await _host.AddSerializableEventsAsync(newEvents, true);
                _eventsProcessed += newEvents.Count;

                // Mark all events as processed
                foreach (var ev in newEvents)
                {
                    TrackProcessedEventId(ev.Id);
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
            if (_persistBatchSize > 0 && newEvents.Count >= _persistBatchSize)
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
        if (_catchUpProgress.IsActive)
        {
            return;
        }

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
                TrackProcessedEventId(ev.Id);
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

            if (!_catchUpProgress.IsActive)
            {
                // Trigger safe promotion after processing buffered events.
                // During catch-up, this path contends with projection apply on the same host instance.
                await TriggerSafePromotion();
            }
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

        var newEvents = list.Where(e => !_processedEventIds.Contains(e.Id)).ToList();
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
