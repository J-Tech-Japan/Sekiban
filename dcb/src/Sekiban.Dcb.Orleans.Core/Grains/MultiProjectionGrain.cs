using System;
using Sekiban.Dcb.Runtime.Native;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.ColdEvents;
using System.Text;
using System.Runtime;
using System.Runtime.ExceptionServices;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Simplified pure infrastructure grain with minimal business logic
///     Demonstrates separation of concerns
/// </summary>
public class MultiProjectionGrain : Grain, IMultiProjectionGrain, ILifecycleParticipant<IGrainLifecycle>
{
    private const int StreamingCatchUpApplyChunkSize = 4096;
    private const string EmptyLogValue = "empty";
    private const int CatchUpEventTypeSummaryTopN = 5;
    private const long CatchUpInformationElapsedThresholdMs = 1000;
    private const int HotCatchUpPersistMaxFetchedEvents = 5_000;
    private static readonly TimeSpan HotCatchUpPersistMaxInterval = TimeSpan.FromMinutes(5);
    private const string PersistOutcomeNotAttempted = "not_attempted";
    private const string PersistOutcomeDurableWrite = "durable_write";
    private const string PersistOutcomeNoDurableWrite = "no_durable_write";
    private const int DefaultSnapshotEnvelopeSizeLimitBytes = 2 * 1024 * 1024;
    private const int SnapshotEnvelopeBase64ExpansionNumerator = 3;
    private const int SnapshotEnvelopeBase64ExpansionDenominator = 4;
    private const int SnapshotEnvelopeReservedOverheadBytes = 16 * 1024;
    private readonly IProjectionActorHostFactory _actorHostFactory;

    // The merged actor options used to create the host on activation, retained so an operator reset can recreate a
    // fresh host in-activation for a full rebuild without waiting for a deactivation cycle.
    private GeneralMultiProjectionActorOptions? _mergedActorOptions;

    // Activation-local coordinator serialising EVERY external IMultiProjectionStateStore mutation (snapshot upsert and
    // the reset's delete) and rejecting an upsert while faulted. Acquired AFTER the grain-state write gate wherever both
    // are held (reset), never before, so there is no lock cycle: upserts take only this coordinator; grain-state writes
    // take only the store gate; the reset takes the store gate then this coordinator. Initialised in the constructor.
    private readonly ExternalStoreCoordinator _externalStore;
    private readonly IEventStore _eventStore;

    // The grain does NOT retain the raw IPersistentState it is injected with: it hands it to this store in the
    // constructor and reaches persisted state only through here, so nothing but the store can call WriteStateAsync.
    private readonly CoordinatedGrainStateStore _stateStore;

    // The live-runtime LastPosition. It advances as events are processed, ahead of any persist (a checkpoint clears the
    // persisted LastPosition), so it is kept as an ephemeral grain field rather than mutating the persisted payload
    // outside the coordinator. Seeded from the committed state on activation; surfaced by status queries.
    private string? _liveLastPosition;

    // SEK-G18/G21: the sole resolver for catch-up START positions. A restored record position is leased exactly once
    // across both the timer and in-call paths and cannot be displaced by host-payload inference while pending.
    private readonly CatchUpStartPositionLeaseResolver _catchUpStartPositions = new();

    // SEK-G18 (#1086): the last SafeWindowThreshold persisted (seeded verbatim from the restored checkpoint record). While
    // the safe checkpoint position is unchanged, the persist writes THIS value verbatim rather than a fresh wall-clock
    // threshold, so a no-progress restart preserves the checkpoint's threshold exactly instead of drifting.
    private string? _lastPersistedSafeWindowThreshold;

    private readonly IEventSubscriptionResolver _subscriptionResolver;
    private readonly IMultiProjectionStateStore? _multiProjectionStateStore;
    private readonly IProjectionStatusStore? _projectionStatusStore;
    private readonly ProjectionStatusOptions _projectionStatusOptions;
    private readonly string _activationId = Guid.CreateVersion7().ToString("N");
    private ProjectionStatusWriterIdentity? _projectionStatusWriterIdentity;
    private long _projectionStatusSequence;
    private int _projectionStatusDirty = 1;
    private int _projectionStatusWriteInProgress;
    private int _projectionStatusFailureAttempt;
    private DateTimeOffset _projectionStatusNextAttemptUtc;
    private DateTimeOffset _projectionStatusLastFailureLogUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _projectionStatusLastConflictLogUtc = DateTimeOffset.MinValue;
    private IDisposable? _projectionStatusTimer;
    private readonly object _projectionStatusCursorGate = new();
    private string? _lastAppliedSortableUniqueId;
    private string? _lastTraversedSortableUniqueId;

    // SEK-G20: the SOLE checkpoint-mutation coordinator. It — not the grain — holds the generation/tombstone CAS surface
    // and is the only type that calls any checkpoint mutation (CAS or legacy). It owns the adopted-token + rebuilt-pending
    // state and arms this grain's query barrier on a tombstone rejection. Null only when no external store is configured.
    private CheckpointMutationCoordinator? _checkpointMutation;
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
    private bool _restoreRetirementFailed;

    // A known-record restore failure durably clears the four-field integrity watermark before replay begins. A
    // zero-progress deactivation in that same activation must not repopulate only the payload-size portions of that
    // watermark; keep the retired all-zero bundle until a fresh safe checkpoint is durably established.
    private bool _retiredWatermarkAwaitingFreshSafeCheckpoint;

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

    // SEK-G14: the projection fault this grain is stuck on. Captured from the actor when a catch-up/stream apply
    // fails, persisted into grain state, and restored on activation so a fresh grain fails queries from the first one.
    private ProjectionFaultDescriptor? _projectionFault;
    private bool _faultPersistFailed;

    // Set on activation when NO durable fault was restored. The first query on such an activation must synchronously
    // catch up to the event-store head before answering, so a fault whose descriptor was lost (a process loss while
    // persistence was failing) is re-established BEFORE any success — closing the restart window the packet forbids.
    // It is per-activation and consumed by the first query only; an already-active projection's ordinary lag is
    // untouched. The single-flight sharing (concurrent first callers await one task; failure is retryable; success is
    // sticky) lives in the gate component, which a friend test drives directly.
    private readonly FirstQueryCatchUpGate _firstQueryGate = new();

    // The last event-store read exception the in-call catch-up swallowed (ProcessSerializableBatch launders a failed
    // read into an empty batch for the resilient background path). The first-query barrier consults it to fail closed
    // with the original exception when catch-up did not reach the head, instead of answering empty success.
    private Exception? _catchUpReadException;
    private IGrainTimer? _faultPersistRetryTimer;
    private int _faultPersistRetryAttempt;
    private static readonly TimeSpan FaultPersistRetryBase = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FaultPersistRetryCap = TimeSpan.FromSeconds(30);

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
        public CatchUpStartPositionLease? StartLease { get; set; }
        public SortableUniqueId? InitialPosition { get; set; }
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
    // Serialises timer and in-call catch-up writers for this activation. Interleaving timers may wait here, but a
    // superseded timer run can never write into the progress object installed by a first-query invocation.
    private readonly CatchUpRunExecutionGate _catchUpExecutionGate = new();
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
    private HybridReadBatchMetadata? _lastHybridReadBatchMetadata;
    private long _eventsProcessedSinceLastCatchUpPersist;
    private long _eventsFetchedSinceLastCatchUpPersist;
    private DateTime _lastCatchUpPersistUtc = DateTime.UtcNow;
    private bool? _lastCatchUpUsedCold;
    private long _catchUpBatchSkipCount;
    private string _lastPersistOutcome = PersistOutcomeNotAttempted;

    private void ResetCatchUpPersistWindow(bool resetReadPath = false)
    {
        _eventsProcessedSinceLastCatchUpPersist = 0;
        _eventsFetchedSinceLastCatchUpPersist = 0;
        _lastCatchUpPersistUtc = DateTime.UtcNow;
        if (resetReadPath)
        {
            _lastCatchUpUsedCold = null;
        }
    }

    private void ObserveCatchUpReadPath(HybridReadBatchMetadata? metadata)
    {
        var usedCold = metadata?.UsedCold == true;
        if (_lastCatchUpUsedCold.HasValue && _lastCatchUpUsedCold.Value != usedCold)
        {
            // A cold-to-hot (or hot-to-cold) transition starts a new logical catch-up window. The applied count,
            // fetched count, and elapsed-time fallback must move together; carrying only one of them makes the next
            // threshold dependent on the previous storage path.
            ResetCatchUpPersistWindow();
        }

        _lastCatchUpUsedCold = usedCold;
    }

    private sealed record PersistPolicySettings(
        int PersistBatchSize,
        TimeSpan PersistInterval,
        bool SkipPersistWhenSafeCheckpointUnchanged);

    private sealed record CatchUpPersistDecision(
        bool ShouldPersist,
        string Reason);

    private sealed record CatchUpBatchTelemetry(
        int BatchNumber,
        string StartPosition,
        string CurrentPosition,
        string LastAppliedPosition,
        string TargetPosition,
        int RequestedMaxCount,
        int FetchedCount,
        int FilteredCount,
        int AppliedCount,
        int PendingStreamEventsBefore,
        int PendingStreamEventsAfter,
        long ReadElapsedMs,
        long ApplyElapsedMs,
        long PersistElapsedMs,
        long SafePromotionElapsedMs,
        long TotalElapsedMs,
        string ReadSource,
        int ColdEventsRead,
        int HotEventsRead,
        bool ReachedColdSegmentBoundary,
        int SegmentCount,
        bool PersistTriggered,
        string PersistReason,
        string EventTypeSummary)
    {
        public string PersistOutcome { get; init; } = PersistOutcomeNotAttempted;
    }

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

    // Keep the pre-SEK-G24 constructor metadata intact for existing binary consumers. Orleans uses the annotated
    // constructor below when the optional status services are registered; direct callers can continue using this
    // compatibility overload and receive the original no-registry behavior.
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
        : this(
            state,
            actorHostFactory,
            eventStore,
            subscriptionResolver,
            multiProjectionStateStore,
            eventStats,
            actorOptions,
            tempFileSnapshotManager,
            logger,
            eventStoreFactory,
            serviceIdProvider,
            projectionStatusStore: null,
            projectionStatusOptions: null)
    {
    }

    [ActivatorUtilitiesConstructor]
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
        IServiceIdProvider? serviceIdProvider = null,
        IProjectionStatusStore? projectionStatusStore = null,
        ProjectionStatusOptions? projectionStatusOptions = null)
    {
        // Transfer ownership of the injected persistent state to the coordinated store immediately; keep no raw
        // IPersistentState reference on the grain so writes cannot bypass the single-writer gate. The live-fault
        // predicate lets the store suppress a checkpoint while the actor is faulted even before the descriptor persists.
        _stateStore = new CoordinatedGrainStateStore(
            state ?? throw new ArgumentNullException(nameof(state)),
            () => _host?.CurrentFault is not null);
        _externalStore = new ExternalStoreCoordinator(ExternalPersistenceBlockedByFault);
        _actorHostFactory = actorHostFactory ?? throw new ArgumentNullException(nameof(actorHostFactory));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _subscriptionResolver = subscriptionResolver ?? new DefaultOrleansEventSubscriptionResolver();
        _multiProjectionStateStore = multiProjectionStateStore;
        _checkpointMutation = multiProjectionStateStore is null
            ? null
            : new CheckpointMutationCoordinator(multiProjectionStateStore, () => _firstQueryGate.Arm());
        _eventStats = eventStats ?? new Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics();
        _injectedActorOptions = actorOptions;
        _tempFileSnapshotManager = tempFileSnapshotManager;
        _logger = logger ?? NullLogger<MultiProjectionGrain>.Instance;
        _eventStoreFactory = eventStoreFactory;
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
        _projectionStatusStore = projectionStatusStore;
        _projectionStatusOptions = projectionStatusOptions ?? new ProjectionStatusOptions();

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

    private static string BuildCatchUpEventTypeSummary(IEnumerable<string> eventTypes)
    {
        var topTypes = eventTypes
            .Where(eventType => !string.IsNullOrWhiteSpace(eventType))
            .GroupBy(eventType => eventType, StringComparer.Ordinal)
            .Select(group => new { EventType = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.EventType, StringComparer.Ordinal)
            .Take(CatchUpEventTypeSummaryTopN)
            .ToList();

        if (topTypes.Count == 0)
        {
            return EmptyLogValue;
        }

        return string.Join(", ", topTypes.Select(item => $"{item.EventType}:{item.Count}"));
    }

    private static bool ShouldLogCatchUpBatchAtInformation(CatchUpBatchTelemetry telemetry) =>
        telemetry.TotalElapsedMs >= CatchUpInformationElapsedThresholdMs ||
        telemetry.PersistTriggered ||
        telemetry.FilteredCount > 0 ||
        (telemetry.RequestedMaxCount > 0 &&
         telemetry.FetchedCount > 0 &&
         telemetry.FetchedCount < telemetry.RequestedMaxCount &&
         telemetry.FetchedCount * 2 <= telemetry.RequestedMaxCount);

    private void LogCatchUpBatchSummary(string projectorName, CatchUpBatchTelemetry telemetry)
    {
        var logLevel = ShouldLogCatchUpBatchAtInformation(telemetry)
            ? LogLevel.Information
            : LogLevel.Debug;

        _logger.Log(
            logLevel,
            MultiProjectionLogEvents.CatchUpBatchSummary,
            "[{ProjectorName}] Catch-up batch summary. BatchNumber={BatchNumber}, StartPosition={StartPosition}, CurrentPosition={CurrentPosition}, LastAppliedPosition={LastAppliedPosition}, TargetPosition={TargetPosition}, RequestedMaxCount={RequestedMaxCount}, FetchedCount={FetchedCount}, FilteredCount={FilteredCount}, AppliedCount={AppliedCount}, PendingStreamEventsBefore={PendingStreamEventsBefore}, PendingStreamEventsAfter={PendingStreamEventsAfter}, ReadElapsedMs={ReadElapsedMs}, ApplyElapsedMs={ApplyElapsedMs}, PersistElapsedMs={PersistElapsedMs}, SafePromotionElapsedMs={SafePromotionElapsedMs}, TotalElapsedMs={TotalElapsedMs}, ReadSource={ReadSource}, ColdEventsRead={ColdEventsRead}, HotEventsRead={HotEventsRead}, ReachedColdSegmentBoundary={ReachedColdSegmentBoundary}, SegmentCount={SegmentCount}, PersistTriggered={PersistTriggered}, PersistReason={PersistReason}, PersistOutcome={PersistOutcome}, EventTypeSummary={EventTypeSummary}",
            projectorName,
            telemetry.BatchNumber,
            telemetry.StartPosition,
            telemetry.CurrentPosition,
            telemetry.LastAppliedPosition,
            telemetry.TargetPosition,
            telemetry.RequestedMaxCount,
            telemetry.FetchedCount,
            telemetry.FilteredCount,
            telemetry.AppliedCount,
            telemetry.PendingStreamEventsBefore,
            telemetry.PendingStreamEventsAfter,
            telemetry.ReadElapsedMs,
            telemetry.ApplyElapsedMs,
            telemetry.PersistElapsedMs,
            telemetry.SafePromotionElapsedMs,
            telemetry.TotalElapsedMs,
            telemetry.ReadSource,
            telemetry.ColdEventsRead,
            telemetry.HotEventsRead,
            telemetry.ReachedColdSegmentBoundary,
            telemetry.SegmentCount,
            telemetry.PersistTriggered,
            telemetry.PersistReason,
            telemetry.PersistOutcome,
            telemetry.EventTypeSummary);
    }

    private void ResetHybridCatchUpLogging()
    {
        _hybridCatchUpCheckLogged = false;
        _lastHybridReadBatchMetadata = null;
    }

    private static string? NormalizeSortableUniqueId(string? sortableUniqueId) =>
        ProjectionHeadStatusUtilities.NormalizeSortableUniqueId(sortableUniqueId);

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

        if (_restoreRetirementFailed)
        {
            return ResultBox.Error<MultiProjectionState>(CreateRestoreRetirementFailure());
        }

        if (_host == null)
        {
            return ResultBox.Error<MultiProjectionState>(
                new InvalidOperationException("Projection host not initialized"));
        }

        var rebuildBlock = await ResolveRebuildBeforeQueryAsync();
        if (rebuildBlock is not null)
        {
            return ResultBox.Error<MultiProjectionState>(rebuildBlock); // SEK-G18 #6 fail-closed while rebuild pending
        }

        try
        {
            await EnsureFirstQuerySyncCatchUpAsync();
        }
        catch (Exception ex)
        {
            return ResultBox.Error<MultiProjectionState>(ex); // fail closed with the original head/read failure
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
        if (!_skipPersistWhenSafeCheckpointUnchanged)
        {
            return false;
        }

        if (!string.Equals(_stateStore.Committed.ProjectorVersion, projectorVersion, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(_stateStore.Committed.LastSortableUniqueId ?? string.Empty, safePosition ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        if (safeVersion.HasValue)
        {
            return _stateStore.Committed.LastGoodSafeVersion > 0 && safeVersion.Value == _stateStore.Committed.LastGoodSafeVersion;
        }

        return true;
    }

    // SEK-G18 (#1086): the SafeWindowThreshold value to persist. When the safe checkpoint POSITION is unchanged versus the
    // committed checkpoint, the threshold is preserved VERBATIM (the last-persisted value, seeded from the restored record)
    // rather than recomputed from wall-clock — so a restart that writes zero new events persists an IDENTICAL threshold.
    // When the safe checkpoint advances, a fresh threshold is written and remembered.
    private string ResolvePersistedSafeWindowThreshold(PersistCheckpoint checkpoint)
    {
        var fresh = checkpoint.SafeThresholdValue ?? _host!.PeekCurrentSafeWindowThreshold();
        var safeUnchanged = string.Equals(
            _stateStore.Committed.LastSortableUniqueId ?? string.Empty,
            checkpoint.SafePosition ?? string.Empty,
            StringComparison.Ordinal);
        if (safeUnchanged && !string.IsNullOrEmpty(_lastPersistedSafeWindowThreshold))
        {
            return _lastPersistedSafeWindowThreshold;
        }
        _lastPersistedSafeWindowThreshold = fresh;
        return fresh;
    }

    private ResultBox<bool>? TryShortCircuitPersist(string projectorName, PersistCheckpoint checkpoint)
    {
        var lastGoodSafeVersion = _stateStore.Committed.LastGoodSafeVersion;
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

    // SEK-G18 #2: the persisted EventsProcessed is the SAFE-checkpoint count (events reflected in the safe state at
    // SafePosition), NOT the total served count. The external record's LastSortableUniqueId is the safe position, so its
    // EventsProcessed must be the matching safe count: on restore _eventsProcessed is seeded from it (the safe baseline)
    // and the exclusive catch-up from the safe position re-adds the still-unsafe + new events exactly once (no double
    // count). SafeVersion is best-effort metadata; fall back to _eventsProcessed when it is unavailable.
    private long ResolveSafeEventsProcessed(PersistCheckpoint checkpoint) =>
        checkpoint.SafeVersion is { } safeVersion ? safeVersion : _eventsProcessed;

    private async Task<bool> CanSaveToExternalStoreAsync(
        string projectorName,
        string projectorVersion,
        long localSafeEventsProcessed)
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

        // Compare SAFE count vs SAFE count: the external record's EventsProcessed is the count at its safe position, and
        // localSafeEventsProcessed is ours. (A legacy record persisted with the old total count is >= its safe count, so
        // the comparison is at worst conservatively skip-biased against a legacy peer — never a stale overwrite.)
        var latestOptional = latestResult.GetValue();
        if (latestOptional.HasValue &&
            latestOptional.Value is { } latestRecord &&
            latestRecord.EventsProcessed > localSafeEventsProcessed)
        {
            _lastError = $"External store has newer safe state ({latestRecord.EventsProcessed}) than local ({localSafeEventsProcessed})";
            _logger.LogWarning(
                "Skip external store save: latest safe EventsProcessed {LatestEvents} > local {LocalEvents} for {ProjectorName} v{ProjectorVersion}.",
                latestRecord.EventsProcessed,
                localSafeEventsProcessed,
                projectorName,
                projectorVersion);
            return false;
        }

        return true;
    }

    private async Task<bool> RetireIntegrityWatermarkAsync(string projectorName, string failureReason)
    {
        try
        {
            var outcome = await _stateStore.ExecuteWriteAsync(
                GrainStateWriteKind.MetadataMaintenance,
                s =>
                {
                    s.LastGoodSafeVersion = 0;
                    s.LastGoodPayloadBytes = 0;
                    s.LastGoodOriginalSizeBytes = 0;
                    s.LastGoodEventsProcessed = 0;
                });

            if (outcome == GrainStateWriteOutcome.Committed)
            {
                // The external record was positively obtained but could not become a host snapshot. Invalidate that
                // derived record before the ordered rebuild so the existing external-store ordering check remains
                // unchanged and no recovery bypass is needed. The authoritative event stream remains untouched.
                await InvalidateExternalDerivedSnapshotAsync();
                _retiredWatermarkAwaitingFreshSafeCheckpoint = true;
                _logger.LogWarning(
                    "[{ProjectorName}] Integrity watermark retired after known-present snapshot restore failure. "
                    + "Failure={FailureReason}",
                    projectorName,
                    failureReason);
                return true;
            }

            _lastPersistOutcome = PersistOutcomeNoDurableWrite;
            _lastError = $"Integrity watermark retirement was not committed: {outcome}";
            _logger.LogError(
                "[{ProjectorName}] Integrity watermark retirement failed; no durable checkpoint was committed. "
                + "Outcome={Outcome}, Failure={FailureReason}",
                projectorName,
                outcome,
                failureReason);
            return false;
        }
        catch (Exception ex)
        {
            _lastPersistOutcome = PersistOutcomeNoDurableWrite;
            _lastError = $"Integrity watermark retirement failed: {ex.Message}";
            _logger.LogError(
                ex,
                "[{ProjectorName}] Integrity watermark retirement failed; no durable checkpoint was committed. "
                + "Failure={FailureReason}",
                projectorName,
                failureReason);
            return false;
        }
    }

    private InvalidOperationException CreateRestoreRetirementFailure() =>
        new($"Projection activation halted because integrity watermark retirement did not commit: {_activationFailureReason}");

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
        if (!await CanSaveToExternalStoreAsync(projectorName, checkpoint.ProjectorVersion, ResolveSafeEventsProcessed(checkpoint)))
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
            EventsProcessed: ResolveSafeEventsProcessed(checkpoint),
            IsOffloaded: false,
            OffloadKey: null,
            OffloadProvider: null,
            OriginalSizeBytes: tempFileSize,
            CompressedSizeBytes: tempFileSize,
            SafeWindowThreshold: ResolvePersistedSafeWindowThreshold(checkpoint),
            CreatedAt: _stateStore.Committed.LastPersistTime == default
                ? DateTime.UtcNow
                : _stateStore.Committed.LastPersistTime,
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

        var saveResult = await UpsertExternalStateCoordinatedAsync(
            writeRequest,
            uploadStream,
            _injectedActorOptions?.MaxSnapshotSerializedSizeBytes ?? 2 * 1024 * 1024);
        if (!saveResult.IsSuccess)
        {
            // A fault-block is a deliberate skip, not a store failure: log it as such and never report saved.
            if (saveResult.GetException() is ExternalPersistenceBlockedByFaultException)
            {
                _logger.LogDebug(
                    "[{ProjectorName}] External store save skipped: projection is faulted",
                    projectorName);
            }
            else
            {
                _lastError = $"External store save failed: {saveResult.GetException().Message}";
                _logger.LogWarning("[{ProjectorName}] {LastError}", projectorName, _lastError);
            }

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

    public async Task<MultiProjectionHeadStatusSnapshot> GetProjectionHeadStatusAsync()
    {
        await EnsureInitializedAsync();

        var (_, projectorName, _) = GetIdentity();
        var projectorVersion = _stateStore.Committed.ProjectorVersion;
        ProjectionHeadStatus? hostStatus = null;

        if (_host != null)
        {
            hostStatus = await _host.GetProjectionHeadStatusAsync();
            var projectorNameResult = ProjectionHeadStatusUtilities.EnsureProjectorNameConsistency(
                projectorName,
                hostStatus.ProjectorName);
            if (!projectorNameResult.IsSuccess)
            {
                throw projectorNameResult.GetException();
            }

            projectorName = projectorNameResult.GetValue();
            if (!string.IsNullOrWhiteSpace(hostStatus.ProjectorVersion))
            {
                projectorVersion = hostStatus.ProjectorVersion;
            }
        }

        var currentPosition = hostStatus?.Current;
        var consistentPosition = hostStatus?.Consistent;
        var catchUpStatus = hostStatus?.CatchUp;
        var currentLastSortableUniqueId = NormalizeSortableUniqueId(currentPosition?.LastSortableUniqueId)
            ?? NormalizeSortableUniqueId(_catchUpProgress.CurrentPosition?.Value)
            ?? NormalizeSortableUniqueId(_stateStore.Committed.LastSortableUniqueId);
        var consistentLastSortableUniqueId = NormalizeSortableUniqueId(consistentPosition?.LastSortableUniqueId)
            ?? NormalizeSortableUniqueId(_stateStore.Committed.LastSortableUniqueId);

        return new MultiProjectionHeadStatusSnapshot(
            ProjectorName: projectorName,
            ProjectorVersion: projectorVersion,
            CurrentEventVersion: currentPosition?.EventVersion ?? consistentPosition?.EventVersion ?? _stateStore.Committed.LastGoodSafeVersion,
            CurrentLastSortableUniqueId: currentLastSortableUniqueId,
            ConsistentEventVersion: consistentPosition?.EventVersion ?? _stateStore.Committed.LastGoodSafeVersion,
            ConsistentLastSortableUniqueId: consistentLastSortableUniqueId,
            IsCatchUpInProgress: _catchUpProgress.IsActive || catchUpStatus?.IsInProgress == true,
            CatchUpCurrentSortableUniqueId: NormalizeSortableUniqueId(_catchUpProgress.CurrentPosition?.Value)
                ?? NormalizeSortableUniqueId(catchUpStatus?.CurrentSortableUniqueId),
            CatchUpTargetSortableUniqueId: NormalizeSortableUniqueId(_catchUpProgress.TargetPosition?.Value)
                ?? NormalizeSortableUniqueId(catchUpStatus?.TargetSortableUniqueId)
                ?? currentLastSortableUniqueId,
            PendingStreamEventCount: _pendingStreamEvents.Count > 0
                ? _pendingStreamEvents.Count
                : catchUpStatus?.PendingStreamEventCount ?? 0);
    }

    /// <summary>
    ///     Get health status for monitoring and diagnostics.
    ///     This method is safe to call even before initialization completes.
    /// </summary>
    public Task<MultiProjectionHealthStatus> GetHealthStatusAsync()
    {
        // Safe access - return defaults if not initialized
        var lastPersistTime = _stateStore.Committed.LastPersistTime;
        var lastSortableUniqueId = _stateStore.Committed.LastSortableUniqueId;
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

        if (_restoreRetirementFailed)
        {
            return ResultBox.Error<string>(CreateRestoreRetirementFailure());
        }

        if (_host == null)
        {
            return ResultBox.Error<string>(
                new InvalidOperationException("Projection host not initialized"));
        }

        var rebuildBlock = await ResolveRebuildBeforeQueryAsync();
        if (rebuildBlock is not null)
        {
            return ResultBox.Error<string>(rebuildBlock); // SEK-G18 #6 fail-closed while rebuild pending
        }

        try
        {
            await EnsureFirstQuerySyncCatchUpAsync();
        }
        catch (Exception ex)
        {
            return ResultBox.Error<string>(ex); // fail closed with the original head/read failure
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

        if (_restoreRetirementFailed)
        {
            throw CreateRestoreRetirementFailure();
        }

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

            var lastApplied = newEvents
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
                .Last()
                .SortableUniqueIdValue;
            MarkProjectionStatusDirty(lastApplied, lastApplied);

            // Mark events as processed
            foreach (var ev in newEvents)
            {
                TrackProcessedEventId(ev.Id);
            }

            if (newEvents.Count > 0)
            {
                _lastEventTime = DateTime.UtcNow;
            }

            await RebuildProjectionIfSignaledAsync();
        }
    }

    // SEK-G18: the dual-state accessor signals RebuildRequired when a live event promoted to safe OUT of global
    // SortableUniqueId order versus the held safe head — which the incremental (compacted-baseline) path cannot reorder.
    // The single mandated remedy is a full ordered rebuild from the authoritative event store. NOT the G14 fault path —
    // that is reserved for failures OF the rebuild itself.
    private async Task RebuildProjectionIfSignaledAsync()
    {
        if (_host is IRebuildSignalingHost { RebuildRequired: true })
        {
            await TriggerDurableFullRebuildAsync();
        }
    }

    // SEK-G18 #6: durably establish the rebuild BEFORE discarding the live host. First arm the first-query barrier so no
    // query can serve the stale pre-rebuild payload; then, inside the single-writer gate, invalidate the derived EXTERNAL
    // snapshot (beforeWrite) and clear the persisted grain-state checkpoint copy-on-write. Only after that durable clear
    // commits is the live host recreated and the barrier re-armed, so the next state/scalar/list query synchronously
    // replays the full ordered history from the authoritative store before it can answer. A process/silo loss at ANY point
    // after the external snapshot is invalidated leaves a fresh activation with no snapshot to restore, forcing the same
    // full replay. If the durable transition itself fails, the barrier stays armed and the live host keeps RebuildRequired,
    // so queries remain fail-closed and the next apply/query retries — never a stale success. The authoritative store is
    // read in SortableUniqueId order, so the from-beginning replay does not re-trip the guard; a poison event on replay
    // establishes the G14 persisted fault via the existing per-event boundary.
    private async Task TriggerDurableFullRebuildAsync()
    {
        // Block queries in THIS activation before touching any durable state.
        _firstQueryGate.Arm();

        // SEK-G20: capture the FULL offending-event context (id + position) behind the rebuild signal BEFORE the host is
        // recreated (which clears it). It is written into the durable marker, and — on a non-capable store — into the G14
        // fault descriptor for the fail-closed fallback.
        var offendingEventId = (_host as IRebuildSignalingHost)?.RebuildOffendingEventId;
        var offendingPosition = (_host as IRebuildSignalingHost)?.RebuildOffendingPosition;

        // SEK-G20 FAIL-CLOSED FALLBACK: a retrograde rebuild against an EXTERNAL store that does NOT support the
        // generation/tombstone CAS cannot be made cross-cluster safe (a peer's unconditional write could re-contaminate).
        // Rather than silently rebuild (the G18 single-cluster behavior, unsafe when the store is shared), enter the G14
        // persisted-fault path with FULL context so the projection fails closed and an operator reset is required.
        if (_multiProjectionStateStore is not null && _checkpointMutation is { IsCapable: false })
        {
            var fault = new ProjectionFaultDescriptor(
                EventId: Guid.TryParse(offendingEventId, out var oid) ? oid : Guid.Empty,
                EventType: string.Empty,
                ProjectorName: GetProjectorName(),
                Position: offendingPosition ?? string.Empty,
                Message: "SEK-G20: a retrograde full rebuild is required but the checkpoint store does not support the "
                    + "generation/tombstone CAS capability; failing closed (operator reset required) rather than risk a "
                    + "cross-cluster stale re-contamination.",
                FaultedAtUtc: DateTime.UtcNow.Ticks);
            _projectionFault = fault;
            try
            {
                await _stateStore.ExecuteWriteAsync(GrainStateWriteKind.FaultDescriptor, s => WriteFaultIntoState(s, fault));
            }
            catch (Exception ex)
            {
                _lastError = $"Projection non-capable-store rebuild fault persist failed: {ex.Message}";
            }
            PinFaultedActivation();
            _firstQueryGate.Arm();
            return;
        }

        // (1) FAIL-FIRST durable marker: commit RebuildRequired=true to grain state BEFORE the external snapshot is
        //     touched. A process/silo loss AFTER this commit is safe — a fresh activation observes the marker (checked
        //     before restore) and forces a full ordered replay even though the external snapshot is still intact. If the
        //     marker commit fails, the external snapshot is NOT invalidated (nothing half-done): pin the activation so it
        //     does not deactivate into a crash window, keep the barrier armed and the live host's RebuildRequired set, and
        //     the next apply/query retries.
        GrainStateWriteOutcome markerOutcome;
        try
        {
            markerOutcome = await _stateStore.ExecuteWriteAsync(
                GrainStateWriteKind.MetadataMaintenance,
                s =>
                {
                    s.RebuildRequired = true;
                    s.RebuildOffendingEventId = offendingEventId;
                    s.RebuildOffendingPosition = offendingPosition;
                });
        }
        catch (Exception ex)
        {
            _lastError = $"Projection rebuild marker persist failed: {ex.Message}";
            PinFaultedActivation();
            _firstQueryGate.Arm();
            return;
        }

        if (markerOutcome != GrainStateWriteOutcome.Committed)
        {
            _lastError = "Projection rebuild marker was not committed.";
            PinFaultedActivation();
            _firstQueryGate.Arm();
            return;
        }

        // (2) Marker is durable. Invalidate the derived EXTERNAL snapshot and clear the derived checkpoint (the marker
        //     STAYS true — a failure here still leaves a durable rebuild requirement, so a crash rebuilds correctly).
        try
        {
            await InvalidateExternalDerivedSnapshotAsync();
            await _stateStore.ExecuteWriteAsync(GrainStateWriteKind.OperatorReset, ResetDerivedStateForFullRebuild);
        }
        catch (Exception ex)
        {
            _lastError = $"Projection rebuild external/checkpoint clear failed: {ex.Message}";
            _firstQueryGate.Arm();
            return; // marker durable; the barrier stays armed and a retry / fresh activation completes the rebuild.
        }

        // (3) Recreate the live host + re-arm the barrier so the next state/scalar/list query synchronously replays the
        //     full ordered history from the authoritative store before it can answer. The durable marker is cleared only
        //     after the rebuilt checkpoint is durably committed (in the persist path).
        RecreateHostForFullRebuild();
    }

    // SEK-G18 #6: called at the start of EVERY state/scalar/list query, before the first-query barrier. If the live host is
    // signalling RebuildRequired, drive the durable transition. On marker-commit success the host is recreated (live
    // RebuildRequired cleared) and this returns null so the query proceeds into the barrier's full ordered replay. While
    // the marker store write keeps failing, the live host retains RebuildRequired and this returns a fail-closed error so
    // the query NEVER serves the stale pre-rebuild payload — the activation stays pinned (no discard/deactivation) and the
    // external snapshot is untouched, and the next query retries. (option (c): a marker-commit failure plus a simultaneous
    // hard crash is the documented known residual.)
    private async Task<Exception?> ResolveRebuildBeforeQueryAsync()
    {
        if (_host is not IRebuildSignalingHost { RebuildRequired: true })
        {
            return null;
        }

        await TriggerDurableFullRebuildAsync();

        if (_host is IRebuildSignalingHost { RebuildRequired: true })
        {
            return new InvalidOperationException(
                "Projection rebuild is pending: the durable rebuild marker is not yet committed, so the query fails "
                + "closed rather than serve a stale pre-rebuild result.");
        }

        return null;
    }

    public async Task<MultiProjectionGrainStatus> GetStatusAsync()
    {
        var currentPosition = _liveLastPosition;
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
        _lastPersistOutcome = PersistOutcomeNotAttempted;
        if (_restoreRetirementFailed)
        {
            _lastPersistOutcome = PersistOutcomeNoDurableWrite;
            return ResultBox.Error<bool>(CreateRestoreRetirementFailure());
        }

        try
        {
            var startUtc = DateTime.UtcNow;
            var projectorName = GetProjectorName();

            if (_host == null)
            {
                _lastPersistOutcome = PersistOutcomeNoDurableWrite;
                return ResultBox.Error<bool>(new InvalidOperationException("Projection host not initialized"));
            }

            // Whatever faulted the actor — the background timer, the stream, or RefreshAsync's own loop — capture it
            // into the state now, so the persisted snapshot re-establishes the fault on the next activation. A faulted
            // projection makes no safe-checkpoint progress, so the snapshot persist below would short-circuit and never
            // write the grain state; write it here directly so the fault survives a restart regardless.
            if (_host.CurrentFault is { } liveFault)
            {
                _projectionFault = liveFault;
                await _stateStore.ExecuteWriteAsync(GrainStateWriteKind.FaultDescriptor, s => WriteFaultIntoState(s, liveFault));
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
                _lastPersistOutcome = PersistOutcomeNoDurableWrite;
                return shortCircuit;
            }

            // Use streaming path when enabled and temp file manager is available
            if (_useStreamingSnapshotIO && _tempFileSnapshotManager is not null)
            {
                return await PersistStateStreamingAsync(projectorName, checkpoint);
            }

            // Get snapshot as opaque bytes from the host
            await using var snapshotStream = new MemoryStream();
            var snapshotWriteResult = await _host.WriteSnapshotForPersistenceToStreamAsync(
                snapshotStream,
                canGetUnsafeState: false,
                offloadThresholdBytes: GetSnapshotPayloadOffloadThresholdBytes(),
                CancellationToken.None);
            if (!snapshotWriteResult.IsSuccess)
            {
                _lastError = snapshotWriteResult.GetException().Message;
                _logger.LogWarning("[{ProjectorName}] {LastError}", projectorName, _lastError);
                _lastPersistOutcome = PersistOutcomeNoDurableWrite;
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
                                         await CanSaveToExternalStoreAsync(projectorName, projectorVersion, ResolveSafeEventsProcessed(checkpoint));

            // v10: Save to external store (Postgres/Cosmos) if available
            if (_multiProjectionStateStore != null && allowExternalStoreSave)
            {
                snapshotStream.Position = 0;
                var writeRequest = new MultiProjectionStateWriteRequest(
                    ProjectorName: projectorName,
                    ProjectorVersion: projectorVersion,
                    PayloadType: typeof(SerializableMultiProjectionStateEnvelope).FullName!,
                    LastSortableUniqueId: safePosition ?? string.Empty,
                    EventsProcessed: ResolveSafeEventsProcessed(checkpoint),
                    IsOffloaded: false,
                    OffloadKey: null,
                    OffloadProvider: null,
                    OriginalSizeBytes: originalSizeBytes,
                    CompressedSizeBytes: compressedSizeBytes,
                    SafeWindowThreshold: ResolvePersistedSafeWindowThreshold(checkpoint),
                    CreatedAt: _stateStore.Committed.LastPersistTime == default
                        ? DateTime.UtcNow
                        : _stateStore.Committed.LastPersistTime,
                    UpdatedAt: DateTime.UtcNow,
                    BuildSource: "GRAIN",
                    BuildHost: Environment.MachineName);

                var saveResult = await UpsertExternalStateCoordinatedAsync(
                    writeRequest,
                    snapshotStream,
                    _injectedActorOptions?.MaxSnapshotSerializedSizeBytes ?? 2 * 1024 * 1024);
                if (!saveResult.IsSuccess)
                {
                    // externalStoreSaved stays false: no LastGood/persisted metadata may advance after this rejection.
                    // A fault-block is a deliberate skip, not a store failure — log accordingly and never report saved.
                    if (saveResult.GetException() is ExternalPersistenceBlockedByFaultException)
                    {
                        _logger.LogDebug(
                            "[{ProjectorName}] External store save skipped: projection is faulted",
                            projectorName);
                    }
                    else
                    {
                        _lastError = $"External store save failed: {saveResult.GetException().Message}";
                        _logger.LogWarning("[{ProjectorName}] {LastError}", projectorName, _lastError);
                    }
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

            // v9: Update Orleans state with key info only (auxiliary/monitoring). Assignment runs UNDER the write gate
            // (inside WriteOrleansStateWithRetryAsync -> ExecuteWriteAsync), so it commits atomically with the write and
            // cannot interleave with a concurrent fault-descriptor persist.
            void ApplyPersistFields(MultiProjectionGrainState s)
            {
                s.ProjectorName = projectorName;
                s.ProjectorVersion = projectorVersion;
                s.LastSortableUniqueId = safePosition;
                s.EventsProcessed = _eventsProcessed;
                s.LastPersistTime = DateTime.UtcNow;

                // Update LastGood fields only when the external store save succeeded.
                if (externalStoreSaved)
                {
                    if (safeVersion is > 0)
                    {
                        s.LastGoodSafeVersion = safeVersion.Value;
                    }

                    if (!_retiredWatermarkAwaitingFreshSafeCheckpoint || safeVersion is > 0)
                    {
                        if (envelopeSize > 0)
                        {
                            s.LastGoodPayloadBytes = envelopeSize;
                        }
                        if (originalSizeBytes > 0)
                        {
                            s.LastGoodOriginalSizeBytes = originalSizeBytes;
                        }
                        s.LastGoodEventsProcessed = _eventsProcessed;

                        // SEK-G18 #6: the rebuilt checkpoint is now durably committed to the external store, so the durable
                        // rebuild marker can be cleared — a subsequent activation may safely restore this fresh checkpoint.
                        s.RebuildRequired = false;
                        s.RebuildOffendingEventId = null;
                        s.RebuildOffendingPosition = null;
                    }
                }

                // Clear legacy fields
                s.SerializedState = null;
                s.StateSize = 0;
                s.SafeLastPosition = null;
                s.LastPosition = null;
            }

            await WriteOrleansStateWithRetryAsync(projectorName, ApplyPersistFields);
            if (externalStoreSaved && safeVersion is > 0)
            {
                _retiredWatermarkAwaitingFreshSafeCheckpoint = false;
            }
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

            _lastPersistOutcome = externalStoreSaved
                ? PersistOutcomeDurableWrite
                : PersistOutcomeNoDurableWrite;
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _lastError = $"Persistence failed: {ex.Message}";
            _logger.LogError(ex, "[{ProjectorName}] Persistence failed", GetProjectorName());
            _lastPersistOutcome = PersistOutcomeNoDurableWrite;
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
                var writeResult = await _host!.WriteSnapshotForPersistenceToStreamAsync(
                    tempStream,
                    canGetUnsafeState: false,
                    offloadThresholdBytes: GetSnapshotPayloadOffloadThresholdBytes(),
                    CancellationToken.None);
                if (!writeResult.IsSuccess)
                {
                    _lastError = writeResult.GetException().Message;
                    _logger.LogWarning("[{ProjectorName}] Streaming snapshot write failed: {Error}", projectorName, _lastError);
                    _lastPersistOutcome = PersistOutcomeNoDurableWrite;
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

                // Step 4: Update Orleans state. Assignment runs UNDER the write gate (via ExecuteWriteAsync) so it
                // commits atomically with the write and cannot interleave with a concurrent fault-descriptor persist.
                void ApplyPersistFields(MultiProjectionGrainState s)
                {
                    s.ProjectorName = projectorName;
                    s.ProjectorVersion = projectorVersion;
                    s.LastSortableUniqueId = safePosition;
                    s.EventsProcessed = _eventsProcessed;
                    s.LastPersistTime = DateTime.UtcNow;

                    if (externalStoreSaved)
                    {
                    if (safeVersion is > 0)
                    {
                        s.LastGoodSafeVersion = safeVersion.Value;
                    }
                    if (!_retiredWatermarkAwaitingFreshSafeCheckpoint || safeVersion is > 0)
                    {
                        if (tempFileSize > 0)
                            s.LastGoodPayloadBytes = tempFileSize;
                        if (tempFileSize > 0)
                            s.LastGoodOriginalSizeBytes = tempFileSize;
                        s.LastGoodEventsProcessed = _eventsProcessed;
                    }
                    }

                    s.SerializedState = null;
                    s.StateSize = 0;
                    s.SafeLastPosition = null;
                    s.LastPosition = null;
                }

                await WriteOrleansStateWithRetryAsync(projectorName, ApplyPersistFields);
                if (externalStoreSaved && safeVersion is > 0)
                {
                    _retiredWatermarkAwaitingFreshSafeCheckpoint = false;
                }
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

                _lastPersistOutcome = externalStoreSaved
                    ? PersistOutcomeDurableWrite
                    : PersistOutcomeNoDurableWrite;
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
            _lastPersistOutcome = PersistOutcomeNoDurableWrite;
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
        Action<MultiProjectionGrainState> applyFields)
    {
        const int maxRetries = 3;
        for (var retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                // applyFields runs INSIDE the store gate, immediately before the write commits — the mutation and the
                // write are one atomic gated operation. On an ETag conflict we re-read and the loop re-applies the same
                // fields onto the refreshed state, so the retry re-derives its mutation under the gate too.
                await _stateStore.ExecuteWriteAsync(GrainStateWriteKind.Checkpoint, applyFields);
                break;
            }
            catch (global::Orleans.Storage.InconsistentStateException) when (retry < maxRetries - 1)
            {
                _logger.LogWarning(
                    "[{ProjectorName}] ETag conflict on Orleans state write (attempt {Attempt}/{MaxAttempts}), re-reading state...",
                    projectorName, retry + 1, maxRetries);
                await _stateStore.ReadStateAsync();
                await Task.Delay(50 * (retry + 1));
            }
        }
    }

    private int GetSnapshotPayloadOffloadThresholdBytes()
    {
        var envelopeLimit = _injectedActorOptions?.MaxSnapshotSerializedSizeBytes
            ?? DefaultSnapshotEnvelopeSizeLimitBytes;
        if (envelopeLimit <= 0)
        {
            return DefaultSnapshotEnvelopeSizeLimitBytes;
        }

        var adjustedLimit = Math.Max(1L, envelopeLimit - SnapshotEnvelopeReservedOverheadBytes);
        var derivedThreshold = adjustedLimit * SnapshotEnvelopeBase64ExpansionNumerator
            / SnapshotEnvelopeBase64ExpansionDenominator;
        return (int)Math.Clamp(derivedThreshold, 1L, int.MaxValue);
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

        if (_restoreRetirementFailed)
        {
            return;
        }

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
        if (_restoreRetirementFailed)
        {
            throw CreateRestoreRetirementFailure();
        }

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
            var rebuildBlock = await ResolveRebuildBeforeQueryAsync();
            if (rebuildBlock is not null)
            {
                throw rebuildBlock; // SEK-G18 #6 fail-closed while rebuild pending
            }

            await EnsureFirstQuerySyncCatchUpAsync();

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
        if (_restoreRetirementFailed)
        {
            throw CreateRestoreRetirementFailure();
        }

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
            var rebuildBlock = await ResolveRebuildBeforeQueryAsync();
            if (rebuildBlock is not null)
            {
                throw rebuildBlock; // SEK-G18 #6 fail-closed while rebuild pending
            }

            await EnsureFirstQuerySyncCatchUpAsync();

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

    public async Task RefreshAsync() =>
        _ = await RefreshWithAuthoritativeCursorAsync(forceEvenIfCatchUpActive: false);

    // forceEvenIfCatchUpActive: the first-query barrier needs the in-call catch-up to RUN even when a background
    // catch-up (re)started by EnsureInitializedAsync has flipped IsActive on — otherwise the barrier would return
    // without reading and could not re-establish a fault or reach the head. Normal RefreshAsync keeps the guard so a
    // manual refresh does not fight an already-running catch-up.
    private async Task<CatchUpInvocationResult> RefreshWithAuthoritativeCursorAsync(bool forceEvenIfCatchUpActive)
    {
        var projectorName = GetProjectorName();
        _logger.LogDebug("[{ProjectorName}] Refreshing: Re-reading events from event store", projectorName);

        await EnsureInitializedAsync();
        if (_restoreRetirementFailed)
        {
            return new CatchUpInvocationResult(
                new CatchUpStartPositionLease(null, CatchUpStartPositionSource.InferredCheckpoint),
                null);
        }

        if (_host == null)
        {
            return new CatchUpInvocationResult(
                new CatchUpStartPositionLease(null, CatchUpStartPositionSource.InferredCheckpoint),
                null);
        }

        await CatchUpProductionTestHooks.PublishAsync(
            CatchUpProductionHookPoint.InvocationBeforeGate,
            new CatchUpProductionObservation(_serviceId, projectorName, null, null));
        await using (await _catchUpExecutionGate.EnterAsync())
        {
            await CatchUpProductionTestHooks.PublishAsync(
                CatchUpProductionHookPoint.InvocationEnteredGate,
                new CatchUpProductionObservation(_serviceId, projectorName, _catchUpProgress.StartLease, null));
            RecoverStaleCatchUpIfNeeded(projectorName);
            var inheritedStart = forceEvenIfCatchUpActive && _catchUpProgress.IsActive
                ? _catchUpProgress.StartLease
                : null;
            if (_catchUpProgress.IsActive && !forceEvenIfCatchUpActive)
            {
                _logger.LogDebug(
                    "[{ProjectorName}] Refresh skipped because catch-up is already active",
                    projectorName);
                return new CatchUpInvocationResult(
                    _catchUpProgress.StartLease
                    ?? new CatchUpStartPositionLease(
                        _catchUpProgress.InitialPosition,
                        CatchUpStartPositionSource.InferredCheckpoint),
                    null);
            }

            // Refresh is expected to complete catch-up before returning. A forced first-query run inherits the active
            // timer run's START lease before superseding it, so a restored record remains authoritative even when the
            // background path won the one-shot lease race.
            _catchUpTimer?.Dispose();
            _catchUpTimer = null;

            var startLease = inheritedStart
                ?? await _catchUpStartPositions.AcquireAsync(
                    forceFullReplay: false,
                    GetCurrentPositionAsync);
            var currentPosition = startLease.StartPosition;
            MarkProjectionStatusDirty(currentPosition?.Value);
            var invocationReached = currentPosition;
            _catchUpProgress = new CatchUpProgress
            {
                StartLease = startLease,
                InitialPosition = currentPosition,
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
            if (inheritedStart is null)
            {
                // A fresh invocation owns a new logical window. A forced first-query invocation with an inherited
                // active run deliberately keeps the prior window's applied/fetched/time progress.
                ResetCatchUpPersistWindow(resetReadPath: true);
            }
            _catchUpBatchSkipCount = 0;

            MoveBufferedStreamEventsToPending(currentPosition);

            try
            {
                const int maxRefreshBatches = 20000;
                for (var i = 0; i < maxRefreshBatches && _catchUpProgress.IsActive; i++)
                {
                    var batch = await ProcessSingleCatchUpBatch();
                    if (batch.LastFetchedPosition is { } batchCursor &&
                        (invocationReached is null || batchCursor.IsLaterThan(invocationReached)))
                    {
                        invocationReached = batchCursor;
                    }

                    if (batch.FetchedCount == 0)
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
            finally
            {
                // RefreshAsync's in-call loop can fault the actor without going through the background failure handlers,
                // so capture and persist the fault on this production path while preserving the original exception.
                await CaptureAndPersistProjectionFaultIfAnyAsync();
            }

            var result = new CatchUpInvocationResult(startLease, invocationReached);
            await CatchUpProductionTestHooks.PublishAsync(
                CatchUpProductionHookPoint.InvocationCompleted,
                new CatchUpProductionObservation(_serviceId, projectorName, result.Start, result.AuthoritativeReachedPosition));
            return result;
        }
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var projectorName = GetProjectorName();
        _logger.LogInformation(
            MultiProjectionLogEvents.ActivationStarted,
            "Grain activation started: {ProjectorName}",
            projectorName);

        // Adopt the state Orleans populated as the committed baseline for reads. If accessing it throws (corrupt or
        // incompatible), clear and proceed with fresh state.
        try
        {
            _stateStore.AdoptProviderStateAsCommitted();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans state access failed, clearing: {ProjectorName}", projectorName);
            try
            {
                await _stateStore.ClearStateAsync();
            }
            catch
            {
                // Ignore clear errors - we'll proceed with fresh state
            }
        }

        // Create projection host via factory
        bool forceFullCatchUp = false;
        var knownRecordObtained = false;
        var knownRecordFailureHandled = false;
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

            _mergedActorOptions = mergedOptions; // retained so an operator reset can recreate a fresh host in-activation

            _host = _actorHostFactory.Create(
                projectorName,
                mergedOptions,
                _logger);

            CaptureProjectionStatusWriterIdentity(projectorName);
            var projectorVersion = _projectionStatusWriterIdentity!.ProjectorVersion;
            bool restoredFromExternalStore = false;

            // SEK-G18 #6: a durable RebuildRequired marker means the persisted/external checkpoint may be the stale
            // pre-rebuild payload. Checked BEFORE any restore: do NOT restore it — arm the shared query barrier and force a
            // full ordered replay from the authoritative store. The marker is cleared only after the rebuilt checkpoint is
            // durably committed (persist path), so a fresh activation cannot serve stale success in the crash window.
            var durableRebuildPending = _stateStore.Committed is IRebuildMarkerState { RebuildRequired: true };
            if (durableRebuildPending)
            {
                _logger.LogWarning(
                    "Durable rebuild marker set on activation: {ProjectorName} — forcing full ordered replay, skipping restore",
                    projectorName);
                _firstQueryGate.Arm();
                forceFullCatchUp = true;
            }

            // SEK-G20 RESTORE AUTHORITY: on a capable store, read the checkpoint control plane (generation/token/lifecycle)
            // BEFORE binding any payload. A TOMBSTONE means a retrograde rebuild is in flight (possibly by another cluster);
            // do NOT bind the possibly-stale payload — force a full ordered replay and mark this activation to CommitRebuilt
            // on the exact tombstone token. An ACTIVE slot is ADOPTED so the first persist CASes on its exact token, so a
            // stale writer is rejected rather than re-contaminating the shared row.
            var checkpointTombstoned = false;
            if (_checkpointMutation is { IsCapable: true } && !durableRebuildPending)
            {
                var slotResult = await _checkpointMutation.ReadSlotAsync(projectorName, projectorVersion, cancellationToken);
                if (slotResult.IsSuccess)
                {
                    var slot = slotResult.GetValue();
                    if (slot.IsTombstoned)
                    {
                        _logger.LogWarning(
                            "Checkpoint tombstone observed on activation: {ProjectorName} — forcing full ordered replay (rebuilt commit pending)",
                            projectorName);
                        _checkpointMutation.AdoptTombstone(slot);
                        _firstQueryGate.Arm();
                        forceFullCatchUp = true;
                        checkpointTombstoned = true;
                    }
                    else if (slot.IsActive)
                    {
                        _checkpointMutation.AdoptActive(slot);
                    }
                }
            }

            // Restore from external store (Postgres/Cosmos) — skipped when a durable rebuild is pending or a tombstone was
            // observed (the payload under a tombstone is not authoritative).
            if (_multiProjectionStateStore != null && !durableRebuildPending && !checkpointTombstoned)
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
                        knownRecordObtained = true;
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
                            knownRecordFailureHandled = true;
                            if (await RetireIntegrityWatermarkAsync(projectorName, errorMsg))
                            {
                                forceFullCatchUp = true;
                            }
                            else
                            {
                                _restoreRetirementFailed = true;
                            }
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
                                var errorMsg = restoreResult.GetException().Message;
                                _stateRestoreSource = StateRestoreSource.Failed;
                                _activationFailureReason = errorMsg;
                                knownRecordFailureHandled = true;
                                if (await RetireIntegrityWatermarkAsync(projectorName, errorMsg))
                                {
                                    forceFullCatchUp = true;
                                }
                                else
                                {
                                    _restoreRetirementFailed = true;
                                }
                            }
                            else
                            {
                                _eventsProcessed = record.EventsProcessed;
                                MarkProjectionStatusDirty(record.LastSortableUniqueId, record.LastSortableUniqueId);
                                ClearProcessedEventCache();

                                // SEK-G18 (#1086): take the catch-up start verbatim from the restored checkpoint record, so
                                // catch-up reads strictly AFTER the durable safe position (exclusive) without re-folding it.
                                _catchUpStartPositions.Restore(
                                    string.IsNullOrEmpty(record.LastSortableUniqueId)
                                        ? null
                                        : new SortableUniqueId(record.LastSortableUniqueId));

                                // SEK-G18 (#1086): seed the last-persisted SafeWindowThreshold verbatim so a no-progress
                                // re-persist writes the SAME value instead of a fresh wall-clock threshold (no drift).
                                _lastPersistedSafeWindowThreshold = record.SafeWindowThreshold;

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
                        if (_stateStore.Committed.LastGoodSafeVersion > 0)
                        {
                            _logger.LogWarning(
                                "Resetting integrity guard: LastGoodSafeVersion was {LastGood} but external snapshot is missing. "
                                + "This allows catch-up to rebuild and persist a new snapshot. {ProjectorName}",
                                _stateStore.Committed.LastGoodSafeVersion,
                                projectorName);
                            await _stateStore.ExecuteWriteAsync(GrainStateWriteKind.MetadataMaintenance, s =>
                            {
                                s.LastGoodSafeVersion = 0;
                                s.LastGoodPayloadBytes = 0;
                                s.LastGoodOriginalSizeBytes = 0;
                                s.LastGoodEventsProcessed = 0;
                            });
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
                    if (knownRecordObtained && !knownRecordFailureHandled)
                    {
                        knownRecordFailureHandled = true;
                        _stateRestoreSource = StateRestoreSource.Failed;
                        _activationFailureReason = ex.Message;
                        if (await RetireIntegrityWatermarkAsync(projectorName, ex.Message))
                        {
                            forceFullCatchUp = true;
                        }
                        else
                        {
                            _restoreRetirementFailed = true;
                        }
                    }
                    else if (!knownRecordObtained)
                    {
                        _stateRestoreSource = StateRestoreSource.Failed;
                        _activationFailureReason = ex.Message;
                        forceFullCatchUp = true;
                    }
                }
            }
            else if (_multiProjectionStateStore == null)
            {
                _logger.LogDebug(
                    MultiProjectionLogEvents.NoExternalStore,
                    "No external state store configured: {ProjectorName}",
                    projectorName);
            }

            // A known record that failed at any point after record acquisition is not a valid restore baseline. This is
            // especially important when the host restore returned success but stream disposal then threw: discard any
            // partially-restored host only after the watermark retirement has committed, then begin the ordered replay
            // against a fresh host. The retirement-write failure path is deliberately excluded by the flag above.
            if (knownRecordObtained &&
                _stateRestoreSource == StateRestoreSource.Failed &&
                !_restoreRetirementFailed)
            {
                RecreateHostForFullRebuild();
                restoredFromExternalStore = false;
                forceFullCatchUp = true;
            }

            if (!restoredFromExternalStore && !_restoreRetirementFailed)
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
        // Re-establish any persisted fault BEFORE the (fire-and-forget) catch-up starts, so the first query fails
        // closed instead of answering empty/partial success in the window before catch-up re-reaches the poison.
        await RestoreProjectionFaultIfPersistedAsync();

        // If nothing was restored, we cannot be sure a fault is not waiting in the un-caught-up tail (its descriptor
        // may have been lost to a process crash while persistence was failing). The first query must therefore
        // synchronously catch up before it can answer — no fresh-activation empty-success window.
        if (_projectionFault is null && !_restoreRetirementFailed)
        {
            _firstQueryGate.Arm();
        }

        if (!_restoreRetirementFailed)
        {
            _ = CatchUpFromEventStoreAsync(forceFullCatchUp);
        }

        // Auto-start subscription so stream-only projections resume after crashes/restarts.
        if (!_restoreRetirementFailed)
        {
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
        }

        // Mark state restore source based on whether we need full catch-up
        if (_restoreRetirementFailed)
        {
            _activationHealthy = false;
            _logger.LogError(
                MultiProjectionLogEvents.UnhealthyActivation,
                "Grain activation halted: integrity watermark retirement failed; no durable checkpoint was committed: {ProjectorName}, Reason: {Reason}",
                projectorName,
                _activationFailureReason);
        }
        else if (_stateRestoreSource == StateRestoreSource.Failed && _eventsProcessed == 0)
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
        _faultPersistRetryTimer?.Dispose();
        _projectionStatusTimer?.Dispose();

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ResultBox<ProjectionFaultReadResult>> TryGetProjectionFaultAsync()
    {
        try
        {
            await EnsureInitializedAsync();

            // This is intentionally the immutable committed snapshot, never _projectionFault or the host. During a
            // fault-persistence outage the live actor still fails closed while this returns empty: handing out a live
            // token that ResetProjectionFaultAsync is guaranteed to reject would be worse than reporting no COMMITTED
            // descriptor. Conversely, only a null FaultEventId is "none"; any partial/corrupt committed descriptor is
            // a read failure, not a healthy answer.
            var committed = _stateStore.Committed;
            if (committed.FaultEventId is null)
            {
                return ResultBox.FromValue(ProjectionFaultReadResult.NoCommittedFault);
            }

            if (string.IsNullOrWhiteSpace(committed.ProjectorName) ||
                string.IsNullOrWhiteSpace(committed.FaultEventId) ||
                string.IsNullOrWhiteSpace(committed.FaultEventType) ||
                string.IsNullOrWhiteSpace(committed.FaultPosition) ||
                committed.FaultMessage is null ||
                committed.FaultedAtUtcTicks <= 0 ||
                !Guid.TryParse(committed.FaultEventId, out var faultEventId))
            {
                throw new InvalidOperationException(
                    "The committed projection-fault descriptor is incomplete or malformed and cannot be used as a reset token.");
            }

            var firstObservedUtc = new DateTime(committed.FaultedAtUtcTicks, DateTimeKind.Utc);
            return ResultBox.FromValue(new ProjectionFaultReadResult(
                HasFault: true,
                Fault: new ProjectionFaultInfo(
                    committed.ProjectorName,
                    faultEventId,
                    committed.FaultEventType,
                    committed.FaultPosition,
                    committed.FaultMessage,
                    firstObservedUtc)));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProjectionFaultReadResult>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<bool>> ResetProjectionFaultAsync(ResetProjectionFaultRequest request)
    {
        if (request is null)
        {
            return ResultBox.Error<bool>(new ArgumentNullException(nameof(request)));
        }

        await EnsureInitializedAsync();
        if (_host is null)
        {
            return ResultBox.Error<bool>(new InvalidOperationException("Projection host not initialized"));
        }

        GrainStateWriteOutcome outcome;
        try
        {
            // ONE atomic precondition inside the single-writer gate: projector name + fault event id + fault position
            // must all match the CURRENT persisted descriptor (the concurrency authority). A mismatch or missing value
            // in ANY field fails the guard with zero mutation, zero provider write, zero external delete, zero live
            // clear. On a match, still inside the gate, the derived EXTERNAL snapshot is invalidated (beforeWrite) so a
            // fresh activation cannot restore a pre-poison snapshot, then the descriptor + derived checkpoint are
            // durably cleared copy-on-write. A same-token race serialises: the first commits and clears the descriptor,
            // so the second's guard no longer matches and it does nothing.
            outcome = await _stateStore.ExecuteGuardedWriteAsync(
                GrainStateWriteKind.OperatorReset,
                committed => FaultTokenMatchesCommitted(committed, request),
                ResetDerivedStateForFullRebuild,
                beforeWrite: InvalidateExternalDerivedSnapshotAsync);
        }
        catch (Exception ex)
        {
            // The external-snapshot invalidation or the persisted clear write failed: the committed descriptor is
            // retained (rolled back) and the LIVE actor fault is untouched, so state/scalar/list queries stay rejected
            // and fault-persist retry stays coherent.
            _lastError = $"Projection fault reset failed: {ex.Message}";
            return ResultBox.Error<bool>(ex);
        }

        if (outcome != GrainStateWriteOutcome.Committed)
        {
            return ResultBox.Error<bool>(new InvalidOperationException(
                "Projection fault reset rejected: the request token does not match the current persisted fault "
                + "(wrong/missing projector, event id, or position; stale token; or no fault present)."));
        }

        // Durable clear committed (descriptor + derived checkpoint gone; external snapshot invalidated). Only now clear
        // the LIVE actor fault and REBUILD in-activation: recreate a fresh host and re-arm the first-query barrier, so
        // the very next query synchronously catches up from the beginning before it can answer — no early healthy
        // window, no reliance on a deactivation cycle. If the poison remains, the per-event boundary re-establishes and
        // persists the fault again on rebuild (reset never skips or quarantines); a permanent clear is earned only when
        // the same position replays successfully.
        ClearLiveProjectionFault();
        RecreateHostForFullRebuild();
        return ResultBox.FromValue(true);
    }

    private static bool FaultTokenMatchesCommitted(
        IReadOnlyMultiProjectionGrainState committed,
        ResetProjectionFaultRequest request) =>
        // Every request AND persisted field must be present (non-null, non-empty) before any equality check — a null or
        // empty value in any of them is a non-match, never normalized to "". Only then does exact equality decide.
        !string.IsNullOrEmpty(request.ProjectorName) &&
        !string.IsNullOrEmpty(request.FaultEventId) &&
        !string.IsNullOrEmpty(request.FaultPosition) &&
        !string.IsNullOrEmpty(committed.ProjectorName) &&
        !string.IsNullOrEmpty(committed.FaultEventId) &&
        !string.IsNullOrEmpty(committed.FaultPosition) &&
        string.Equals(committed.ProjectorName, request.ProjectorName, StringComparison.Ordinal) &&
        string.Equals(committed.FaultEventId, request.FaultEventId, StringComparison.Ordinal) &&
        string.Equals(committed.FaultPosition, request.FaultPosition, StringComparison.Ordinal);

    // Invalidates the derived EXTERNAL snapshot (Postgres/Cosmos IMultiProjectionStateStore) for the current projector +
    // version, so a fresh activation cannot restore a pre-poison snapshot and the rebuild starts from the beginning.
    // Derived-state only — this never deletes authoritative events. A no-op when no external store is configured.
    private async Task InvalidateExternalDerivedSnapshotAsync()
    {
        if (_multiProjectionStateStore is null || _host is null)
        {
            return;
        }

        // Route the invalidation through the external-store coordinator so any parked/in-flight snapshot upsert completes
        // BEFORE this invalidation, and no upsert runs concurrently with it.
        await _externalStore.InvalidateAsync(PerformCheckpointInvalidationCoreAsync);
    }

    // The SINGLE capability-aware invalidation shared by the retrograde-rebuild path and the admin DeleteExternalStateAsync
    // path — always run inside the external-store coordinator. Delegates to the sole CheckpointMutationCoordinator (durable
    // bump+tombstone CAS on a capable store; legacy delete otherwise). The grain holds no raw checkpoint mutation reach.
    private Task PerformCheckpointInvalidationCoreAsync() =>
        _checkpointMutation!.InvalidateAsync(GetProjectorName(), _host!.GetProjectorVersion());

    // True while a fault exists on the live actor OR the committed persisted descriptor. No external snapshot may be
    // upserted in this state — a faulted projection has no valid derived state, and this stops a stale/late upsert from
    // recreating data a reset is invalidating.
    private bool ExternalPersistenceBlockedByFault() =>
        _host?.CurrentFault is not null || _stateStore.Committed.FaultEventId is not null;

    // The one path every external snapshot upsert (normal persist, streaming persist, version rewrite) goes through:
    // serialised on the external-store coordinator AND rejected while faulted. When faulted it returns a
    // ResultBox.Error carrying ExternalPersistenceBlockedByFaultException (NOT a success carrying false), so every
    // caller inspecting only IsSuccess takes the not-saved branch and cannot report the snapshot as saved or advance
    // persisted metadata after the rejection.
    private Task<ResultBox<bool>> UpsertExternalStateCoordinatedAsync(
        MultiProjectionStateWriteRequest request,
        Stream stream,
        int offloadThreshold)
    {
        if (_checkpointMutation is null)
        {
            return Task.FromResult(ResultBox.FromValue(false));
        }

        // SEK-G20: every product persist goes through the sole CheckpointMutationCoordinator — an EXPECTED-TOKEN CAS on a
        // capable store (a stale writer is ConditionRejected and NEVER re-contaminates the row; a rebuilt commit is one
        // atomic same-row CAS on the exact tombstone token), or the legacy unconditional upsert on a non-capable store.
        // The grain holds no raw checkpoint mutation reach. Serialised on the external-store coordinator gate here.
        return _externalStore.UpsertAsync(() => _checkpointMutation.PersistAsync(request, stream, offloadThreshold));
    }

    // Recreates a fresh actor host in the current activation and re-arms the first-query barrier, so the next query
    // rebuilds the projection from the beginning (external snapshot invalidated, checkpoint cleared). No deactivation
    // cycle is required and no early healthy answer is possible before the barrier's synchronous catch-up.
    private void RecreateHostForFullRebuild()
    {
        _eventsProcessed = 0;
        ClearProcessedEventCache();
        _unsafeEventIds.Clear();
        _eventBuffer.Clear();
        _pendingStreamEvents.Clear();
        CompactRetainedCollections();
        _liveLastPosition = null;
        lock (_projectionStatusCursorGate)
        {
            _lastAppliedSortableUniqueId = null;
            _lastTraversedSortableUniqueId = null;
        }
        Interlocked.Exchange(ref _projectionStatusDirty, 1);
        _projectionFault = null;
        _catchUpTimer?.Dispose();
        _catchUpTimer = null;
        _catchUpProgress = new CatchUpProgress { IsActive = false };
        ResetCatchUpPersistWindow(resetReadPath: true);

        _host = _actorHostFactory.Create(GetProjectorName(), _mergedActorOptions ?? DefaultActorOptions, _logger);
        VerifyPinnedProjectionStatusWriterIdentityAfterHostRecreation();
        _firstQueryGate.Arm();
    }

    private void CaptureProjectionStatusWriterIdentity(string projectorName)
    {
        var host = _host ?? throw new InvalidOperationException("Projection status identity requires an actor host.");
        var identity = new ProjectionStatusWriterIdentity(
            _serviceId,
            projectorName,
            host.GetProjectorVersion(),
            string.IsNullOrWhiteSpace(_projectionStatusOptions.ClusterId)
                ? ProjectionStatusOptions.DefaultClusterId
                : _projectionStatusOptions.ClusterId);

        if (_projectionStatusWriterIdentity is null)
        {
            _projectionStatusWriterIdentity = identity;
            return;
        }

        if (!Equals(_projectionStatusWriterIdentity, identity))
        {
            _logger.LogWarning(
                "Projection status writer identity was already pinned; retaining {PinnedProjectorVersion} instead of {ObservedProjectorVersion}: {ProjectorName}",
                _projectionStatusWriterIdentity.ProjectorVersion,
                identity.ProjectorVersion,
                projectorName);
        }
    }

    private void VerifyPinnedProjectionStatusWriterIdentityAfterHostRecreation()
    {
        if (_projectionStatusWriterIdentity is not { } pinned || _host is null)
        {
            _logger.LogWarning("Projection host was recreated before a projection status writer identity was pinned: {ProjectorName}", GetProjectorName());
            return;
        }

        var recreatedVersion = _host.GetProjectorVersion();
        if (!string.Equals(pinned.ProjectorVersion, recreatedVersion, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Projection host recreation reported version {ObservedProjectorVersion}; retaining activation-pinned status version {PinnedProjectorVersion}: {ProjectorName}",
                recreatedVersion,
                pinned.ProjectorVersion,
                pinned.ProjectorName);
        }
    }

    // Clears the persisted fault descriptor AND the derived projection checkpoint on the candidate, so catch-up rebuilds
    // the projection from the beginning. Pure candidate mutation — no live actor side effect.
    private void ResetDerivedStateForFullRebuild(MultiProjectionGrainState s)
    {
        s.ProjectorName = GetProjectorName();
        s.SerializedState = null;
        s.LastPosition = null;
        s.SafeLastPosition = null;
        s.LastSortableUniqueId = null;
        s.EventsProcessed = 0;
        s.StateSize = 0;
        s.LastGoodSafeVersion = 0;
        s.LastGoodPayloadBytes = 0;
        s.LastGoodOriginalSizeBytes = 0;
        s.LastGoodEventsProcessed = 0;
        s.LastPersistTime = DateTime.UtcNow;
        ClearFaultFieldsOnCandidate(s);
    }

    private void MarkProjectionStatusDirty(
        string? traversedPosition = null,
        string? appliedPosition = null)
    {
        lock (_projectionStatusCursorGate)
        {
            if (!string.IsNullOrWhiteSpace(traversedPosition) &&
                (string.IsNullOrWhiteSpace(_lastTraversedSortableUniqueId) ||
                 string.Compare(traversedPosition, _lastTraversedSortableUniqueId, StringComparison.Ordinal) > 0))
            {
                _lastTraversedSortableUniqueId = traversedPosition;
            }

            if (!string.IsNullOrWhiteSpace(appliedPosition) &&
                (string.IsNullOrWhiteSpace(_lastAppliedSortableUniqueId) ||
                 string.Compare(appliedPosition, _lastAppliedSortableUniqueId, StringComparison.Ordinal) > 0))
            {
                _lastAppliedSortableUniqueId = appliedPosition;
            }
        }

        Interlocked.Exchange(ref _projectionStatusDirty, 1);
    }

    private async Task WriteProjectionStatusHeartbeatAsync()
    {
        if (_projectionStatusStore is null || !_projectionStatusOptions.Enabled ||
            Interlocked.Exchange(ref _projectionStatusWriteInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_projectionStatusNextAttemptUtc > now)
            {
                return;
            }

            var writerIdentity = _projectionStatusWriterIdentity;
            if (writerIdentity is null)
            {
                _logger.LogDebug("Projection status writer identity has not been captured: {ProjectorName}", GetProjectorName());
                return;
            }

            var projectorName = writerIdentity.ProjectorName;
            var sequence = Volatile.Read(ref _projectionStatusSequence) + 1;
            string? lastAppliedPosition;
            string? lastTraversedPosition;
            lock (_projectionStatusCursorGate)
            {
                lastAppliedPosition = _lastAppliedSortableUniqueId;
                lastTraversedPosition = _lastTraversedSortableUniqueId;
            }

            var heartbeat = new ProjectionStatusHeartbeat(
                writerIdentity.ServiceId,
                writerIdentity.ProjectorName,
                writerIdentity.ProjectorVersion,
                writerIdentity.ClusterId,
                _activationId,
                sequence,
                _eventsProcessed,
                lastAppliedPosition ?? _liveLastPosition,
                lastTraversedPosition,
                now)
            {
                Phase = ResolveProjectionStatusPhase(),
                LeaseExpiresAtUtc = now + ResolveProjectionStatusLeaseDuration(),
                IsFaulted = IsProjectionStatusFaulted(),
                FaultMessage = ResolveProjectionStatusFaultMessage()
            };

            var writeTimeout = _projectionStatusOptions.HeartbeatWriteTimeout > TimeSpan.Zero
                ? _projectionStatusOptions.HeartbeatWriteTimeout
                : TimeSpan.FromSeconds(5);
            using var writeTimeoutCts = new CancellationTokenSource(writeTimeout);
            var expectedSequence = Volatile.Read(ref _projectionStatusSequence);
            var writeResult = await _projectionStatusStore.UpsertAsync(
                heartbeat,
                expectedSequence,
                writeTimeoutCts.Token).ConfigureAwait(false);
            if (!writeResult.IsSuccess)
            {
                ScheduleProjectionStatusRetry(now);
                if (ShouldLogProjectionStatusFailure(now, ref _projectionStatusLastFailureLogUtc))
                {
                    _logger.LogDebug(
                        writeResult.GetException(),
                        "[{ProjectorName}] Projection status heartbeat failed; projection execution is unaffected",
                        projectorName);
                }
                return;
            }

            var outcome = writeResult.GetValue();
            if (outcome.Committed)
            {
                Volatile.Write(ref _projectionStatusSequence, outcome.Current?.Sequence ?? sequence);
                Interlocked.Exchange(ref _projectionStatusDirty, 0);
                _projectionStatusFailureAttempt = 0;
                _projectionStatusNextAttemptUtc = DateTimeOffset.MinValue;
            }
            else
            {
                // A missing row after an update attempt is a rebase, not an implicit create. Reset only the local fence;
                // the scheduled next operation will still use the provider-conditional expected=0 create path.
                if (outcome.ConflictDetails?.Reason == ProjectionStatusConflictReason.RowAbsent && expectedSequence > 0)
                {
                    Volatile.Write(ref _projectionStatusSequence, 0);
                }
                // Every other observed row is a normal CAS rebase. Adopt its exact sequence even when it moved backward
                // relative to this activation's stale local fence; the physical identity remains activation-pinned.
                else if (outcome.Current is { Sequence: var currentSequence })
                {
                    Volatile.Write(ref _projectionStatusSequence, currentSequence);
                }

                ScheduleProjectionStatusRetry(now);
                if (ShouldLogProjectionStatusFailure(now, ref _projectionStatusLastConflictLogUtc))
                {
                    _logger.LogWarning(
                        "[{ProjectorName}] Projection status heartbeat CAS conflict: {Reason}",
                        projectorName,
                        outcome.ConflictReason ?? "provider rejected stale sequence");
                }
            }
        }
        catch (Exception ex)
        {
            // Status is observability only.  Await the provider operation, but never allow an outage to fault or slow
            // the projection path.
            ScheduleProjectionStatusRetry(DateTimeOffset.UtcNow);
            var now = DateTimeOffset.UtcNow;
            if (ShouldLogProjectionStatusFailure(now, ref _projectionStatusLastFailureLogUtc))
            {
                _logger.LogDebug(
                    ex,
                    "[{ProjectorName}] Projection status heartbeat exception; projection execution is unaffected",
                    GetProjectorName());
            }
        }
        finally
        {
            Volatile.Write(ref _projectionStatusWriteInProgress, 0);
        }
    }

    private void ScheduleProjectionStatusRetry(DateTimeOffset now)
    {
        var attempt = Math.Min(6, ++_projectionStatusFailureAttempt);
        var retryBase = _projectionStatusOptions.HeartbeatRetryBase > TimeSpan.Zero
            ? _projectionStatusOptions.HeartbeatRetryBase
            : TimeSpan.FromSeconds(1);
        var retryCap = _projectionStatusOptions.HeartbeatRetryCap > TimeSpan.Zero
            ? _projectionStatusOptions.HeartbeatRetryCap
            : TimeSpan.FromSeconds(30);
        var candidateTicks = retryBase.Ticks * Math.Pow(2, attempt - 1);
        var delayTicks = Math.Min(retryCap.Ticks, Math.Max(1, (long)Math.Min(long.MaxValue, candidateTicks)));
        var delay = TimeSpan.FromTicks(delayTicks);
        _projectionStatusNextAttemptUtc = now + delay;
        Interlocked.Exchange(ref _projectionStatusDirty, 1);
    }

    private bool ShouldLogProjectionStatusFailure(DateTimeOffset now, ref DateTimeOffset lastLoggedUtc)
    {
        var interval = _projectionStatusOptions.HeartbeatFailureLogInterval > TimeSpan.Zero
            ? _projectionStatusOptions.HeartbeatFailureLogInterval
            : TimeSpan.FromSeconds(30);
        if (now - lastLoggedUtc < interval)
        {
            return false;
        }

        lastLoggedUtc = now;
        return true;
    }

    private string ResolveProjectionStatusPhase()
    {
        if (IsProjectionStatusFaulted())
        {
            return ProjectionStatusPhases.Faulted;
        }

        if (!_isInitialized)
        {
            return ProjectionStatusPhases.Starting;
        }

        return _catchUpProgress.IsActive
            ? ProjectionStatusPhases.CatchingUp
            : ProjectionStatusPhases.Active;
    }

    private bool IsProjectionStatusFaulted() =>
        _projectionFault is not null ||
        _stateStore.Committed.FaultEventId is not null ||
        !_activationHealthy;

    private string? ResolveProjectionStatusFaultMessage() =>
        _projectionFault?.Message ??
        _stateStore.Committed.FaultMessage ??
        _activationFailureReason;

    private TimeSpan ResolveProjectionStatusLeaseDuration()
    {
        var interval = _projectionStatusOptions.HeartbeatInterval > TimeSpan.Zero
            ? _projectionStatusOptions.HeartbeatInterval
            : TimeSpan.FromSeconds(30);
        return TimeSpan.FromTicks(Math.Max(TimeSpan.FromMinutes(1).Ticks, interval.Ticks * 2));
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

        if (_projectionStatusStore is not null && _projectionStatusOptions.Enabled)
        {
            var interval = _projectionStatusOptions.HeartbeatInterval > TimeSpan.Zero
                ? _projectionStatusOptions.HeartbeatInterval
                : TimeSpan.FromSeconds(30);
            _projectionStatusTimer = this.RegisterGrainTimer(
                async () => await WriteProjectionStatusHeartbeatAsync(),
                new GrainTimerCreationOptions
                {
                    DueTime = TimeSpan.Zero,
                    Period = interval,
                    Interleave = true,
                    KeepAlive = false
                });
        }
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

    /// <summary>
    ///     On the first query of a fresh activation that restored no fault, synchronously catches up to the event-store
    ///     head BEFORE the query can answer, and persists any fault that catch-up re-establishes. A fault whose
    ///     descriptor was lost (a crash while persistence was failing) is therefore re-established before any success,
    ///     closing the restart window. It is per-activation and runs once; an already-active projection's ordinary lag
    ///     is untouched, and it is a fast no-op when the projection is already at head.
    ///     Callers share ONE in-flight barrier task, so concurrent first callers all await the same work and all
    ///     observe the same outcome. A head/read/catch-up failure is never swallowed: the barrier throws (the original
    ///     exception is preserved) and is NOT marked complete, so the query fails closed and the next query retries —
    ///     no caller reaches empty/current state because a flag was cleared early.
    /// </summary>
    private Task EnsureFirstQuerySyncCatchUpAsync() => _firstQueryGate.EnsureAsync(RunFirstQuerySyncCatchUpAsync);

    private async Task RunFirstQuerySyncCatchUpAsync()
    {
        if (_host is null)
        {
            return;
        }

        // Already faulted (a persisted descriptor was restored, or an earlier in-call refresh established it): the
        // query fails on the live fault regardless, so there is nothing to catch up.
        if (_host.CurrentFault is not null)
        {
            return;
        }

        // Read the AUTHORITATIVE event-store head. If we cannot read it, we cannot prove there is no poison in the
        // un-caught-up tail, so we must fail the query closed with the original exception rather than answer empty.
        var catchUpStore = GetCatchUpEventStore();
        var headResult = await catchUpStore.GetLatestSortableUniqueIdAsync();
        if (!headResult.IsSuccess)
        {
            ExceptionDispatchInfo.Capture(headResult.GetException()).Throw();
        }

        var head = headResult.GetValue();
        if (string.IsNullOrEmpty(head))
        {
            return; // empty store: nothing durable to be behind on.
        }

        // A non-empty head must be proven by this invocation's authoritative traversal. In particular, a safe-empty
        // host may already expose a later unsafe cursor received from the stream while an earlier durable event is
        // still missing. Shared unsafe metadata can neither skip this read nor serve as its START checkpoint.
        // Catch up IN-CALL (not on the background timer, which a non-reentrant grain turn held by the query would
        // deadlock waiting on). Refresh re-reads and re-folds the tail; a poison re-faults the actor.
        _catchUpReadException = null;

        CatchUpInvocationResult? invocation = null;
        try
        {
            invocation = await RefreshWithAuthoritativeCursorAsync(forceEvenIfCatchUpActive: true);
        }
        catch when (_host.CurrentFault is not null)
        {
            // A confirmed projection fault was established. The actor is faulted; the query surfaces it as an error.
            // The barrier's job is done — swallow the rethrown reject and complete.
        }

        if (_host.CurrentFault is not null)
        {
            return; // fault established and (via RefreshAsync) persisted; the query will fail closed on it.
        }

        // No fault, but did THIS invocation actually REACH the fixed head? Only the cursor returned by its own store
        // reads is authoritative. The shared timer progress is observability state and may belong to a cancelled or
        // replacement Interleave=true run, so it is deliberately never consulted for this judgment.
        var reached = invocation?.AuthoritativeReachedPosition;
        if (reached is null || string.CompareOrdinal(reached.Value, head) < 0)
        {
            // Still behind: fail closed. Prefer the original read exception (a swallowed failed read) for context; the
            // field is a best-effort signal shared with the resilient background reader, so only trust it here, where
            // we have independently confirmed we did not reach the head.
            if (_catchUpReadException is not null)
            {
                var captured = _catchUpReadException;
                _catchUpReadException = null;
                ExceptionDispatchInfo.Capture(captured).Throw();
            }

            throw new InvalidOperationException(
                $"[{GetProjectorName()}] First-query catch-up did not reach the event-store head " +
                $"(reached '{reached?.Value ?? "beginning"}', head '{head}'); failing the query closed.");
        }

        // Reached the head with no fault: clear any stale read exception a resilient background read may have left.
        _catchUpReadException = null;
    }


    /// <summary>
    ///     Captures the actor's fault and DURABLY persists it before the caller treats the failure as handled. A
    ///     background apply that faulted but only kept the descriptor in memory could lose it to a silo crash before
    ///     the next timer/manual persist, and a fresh activation would then answer success until it re-reached the
    ///     poison. So this awaits the grain-state write.
    ///     If that write fails, it does NOT deactivate. Deactivating would be the worst move: the descriptor never
    ///     persisted, so discarding this activation throws away the only record of the fault, and the fresh activation
    ///     that replaces it — with nothing to restore — answers empty success until a background timer re-reaches the
    ///     poison. Instead this PINS the current activation (it already holds the live fault, so its queries already
    ///     fail) and keeps it alive, so the fault-closed state is retained rather than lost. The activation stays until
    ///     a later persist succeeds (store recovered) or the process is lost; a lost process re-reads the events on
    ///     restart and re-faults, which is the only fail-closed outcome available when the state store itself is down.
    /// </summary>
    private async Task CaptureAndPersistProjectionFaultIfAnyAsync()
    {
        if (_projectionFault is not null)
        {
            return;
        }

        var fault = _host?.CurrentFault;
        if (fault is null)
        {
            return;
        }

        _projectionFault = fault;

        try
        {
            await _stateStore.ExecuteWriteAsync(GrainStateWriteKind.FaultDescriptor, s => WriteFaultIntoState(s, fault));
        }
        catch (Exception writeEx)
        {
            _faultPersistFailed = true;
            _logger.LogCritical(
                writeEx,
                "[{ProjectorName}] Failed to persist projection fault descriptor; pinning this faulted activation and retrying persistence in the background until the descriptor is durable.",
                GetProjectorName());
            PinFaultedActivation();
            ScheduleFaultPersistRetry();
        }
    }

    /// <summary>
    ///     Keeps a faulted activation whose descriptor could not be persisted from being reclaimed and reactivated
    ///     empty. The load-bearing guarantee while pinned is the live in-memory fault, which fails every query; the pin
    ///     only stops that live state from being silently thrown away before the descriptor is made durable.
    /// </summary>
    private void PinFaultedActivation()
    {
        try
        {
            DelayDeactivation(TimeSpan.FromDays(3650));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{ProjectorName}] Could not pin faulted activation.", GetProjectorName());
        }
    }

    /// <summary>
    ///     Registers the product-owned retry: while the fault descriptor could not be persisted, the grain keeps trying
    ///     to write it. Once it succeeds the descriptor is durable — a subsequent fresh activation restores it and
    ///     fails closed — so the pin is released and this activation may be reclaimed normally.
    /// </summary>
    /// <summary>
    ///     Schedules the product-owned retry as a self-rescheduling one-shot with capped exponential backoff and
    ///     bounded jitter — never a fixed hot loop — so a store outage does not become unbounded write pressure or a
    ///     warning-per-tick log flood. The actual write goes through the single grain-state gate, so it cannot race a
    ///     normal persist.
    /// </summary>
    private void ScheduleFaultPersistRetry()
    {
        _faultPersistRetryTimer?.Dispose();

        var exponent = Math.Min(_faultPersistRetryAttempt, 16); // cap the exponent so the shift cannot overflow
        var backoffMs = Math.Min(
            FaultPersistRetryBase.TotalMilliseconds * Math.Pow(2, exponent),
            FaultPersistRetryCap.TotalMilliseconds);
        var jitter = 1.0 + ((_faultPersistRetryAttempt * 7 % 41) - 20) / 100.0; // +/-20%, deterministic per attempt
        var due = TimeSpan.FromMilliseconds(Math.Max(FaultPersistRetryBase.TotalMilliseconds, backoffMs * jitter));

        _faultPersistRetryTimer = this.RegisterGrainTimer(
            RetryFaultPersistAsync,
            new GrainTimerCreationOptions
            {
                DueTime = due,
                Period = Timeout.InfiniteTimeSpan, // one-shot; each attempt reschedules the next
                Interleave = false
            });
    }

    private async Task RetryFaultPersistAsync()
    {
        if (!_faultPersistFailed || _projectionFault is null)
        {
            _faultPersistRetryTimer?.Dispose();
            _faultPersistRetryTimer = null;
            return;
        }

        // Re-apply the descriptor and write again, with the assignment and write together under the gate.
        var fault = _projectionFault;

        try
        {
            await _stateStore.ExecuteWriteAsync(GrainStateWriteKind.FaultDescriptor, s => WriteFaultIntoState(s, fault));
        }
        catch (Exception ex)
        {
            _faultPersistRetryAttempt++;
            // The first failure was already logged at Critical by the caller; keep the retries at Debug so a long
            // outage does not flood the log, and back off before trying again.
            _logger.LogDebug(
                ex,
                "[{ProjectorName}] Fault-descriptor persistence retry {Attempt} still failing; backing off.",
                GetProjectorName(),
                _faultPersistRetryAttempt);
            ScheduleFaultPersistRetry();
            return;
        }

        // Durable now: a fresh activation would restore the fault and fail closed, so the fail-closed guarantee no
        // longer depends on THIS activation staying alive. Release the pin and stop retrying.
        _faultPersistFailed = false;
        _faultPersistRetryAttempt = 0;
        _faultPersistRetryTimer?.Dispose();
        _faultPersistRetryTimer = null;
        try
        {
            DelayDeactivation(TimeSpan.Zero); // undo the pin
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[{ProjectorName}] Could not release activation pin after durable fault persist.", GetProjectorName());
        }

        _logger.LogInformation(
            "[{ProjectorName}] Fault descriptor is now durable; pin released.",
            GetProjectorName());
    }

    // Mutates the passed persisted payload only; the caller runs this inside the store's gate so the assignment and the
    // write commit together.
    private void WriteFaultIntoState(MultiProjectionGrainState? state, ProjectionFaultDescriptor fault)
    {
        if (state is null)
        {
            return;
        }

        // Persist the projector name alongside the fault fields, so the reset token's projector name can be validated
        // against the persisted descriptor even when the fault is the first thing this grain ever persisted (no prior
        // checkpoint set ProjectorName).
        if (!string.IsNullOrEmpty(fault.ProjectorName))
        {
            state.ProjectorName = fault.ProjectorName;
        }

        // A version-scoped restore can only distinguish A from B if the first-ever fault write carries the running
        // version too. Fault persistence is often the first metadata write for a projection, so relying on a later
        // checkpoint to populate this field would silently turn every fresh fault into an unversioned mismatch.
        var projectorVersion = _host?.GetProjectorVersion();
        if (!string.IsNullOrWhiteSpace(projectorVersion))
        {
            state.ProjectorVersion = projectorVersion;
        }

        state.FaultEventId = fault.EventId.ToString();
        state.FaultEventType = fault.EventType;
        state.FaultPosition = fault.Position;
        state.FaultMessage = fault.Message;
        state.FaultedAtUtcTicks = fault.FaultedAtUtc;
    }

    // Clears ONLY the persisted fault fields on the candidate payload — a pure mutation with no live actor side effect,
    // so it is safe to run inside the store's gate on a candidate that may be rolled back. The live actor fault is
    // cleared separately, only after the durable reset write commits (see ClearLiveProjectionFault).
    private static void ClearFaultFieldsOnCandidate(MultiProjectionGrainState state)
    {
        state.FaultEventId = null;
        state.FaultEventType = null;
        state.FaultPosition = null;
        state.FaultMessage = null;
        state.FaultedAtUtcTicks = 0;
    }

    // Non-throwing, idempotent live-fault clear. Called ONLY after the durable reset write has committed. It never
    // throws, so a hiccup here cannot make the caller reinterpret the already-durable write as failed; clearing an
    // already-clear fault is a no-op.
    private void ClearLiveProjectionFault()
    {
        _projectionFault = null;
        try
        {
            _host?.ClearFaultForRebuild();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{ProjectorName}] Live fault clear after a durable reset raised; the persisted reset already committed.", GetProjectorName());
        }

        _liveLastPosition = null;
        lock (_projectionStatusCursorGate)
        {
            _lastAppliedSortableUniqueId = null;
            _lastTraversedSortableUniqueId = null;
        }
        Interlocked.Exchange(ref _projectionStatusDirty, 1);
    }

    /// <summary>
    ///     Re-establishes a persisted fault into the freshly-activated host so the first query fails closed. A fault is
    ///     scoped to the projector version that produced it: if the persisted and running versions differ, clear ONLY
    ///     the descriptor through one awaited metadata commit before catch-up, query/reset, or the admin read can run.
    ///     A failed write deliberately escapes activation; an in-memory-only clear would expose a false healthy state.
    /// </summary>
    private async Task RestoreProjectionFaultIfPersistedAsync()
    {
        var state = _stateStore.Committed;
        if (state?.FaultEventId is null || _host is null)
        {
            return;
        }

        var persistedProjectorVersion = state.ProjectorVersion;
        var runningProjectorVersion = _host.GetProjectorVersion();
        if (!string.Equals(persistedProjectorVersion, runningProjectorVersion, StringComparison.Ordinal))
        {
            try
            {
                var outcome = await _stateStore.ExecuteWriteAsync(
                    GrainStateWriteKind.MetadataMaintenance,
                    ClearFaultFieldsOnCandidate);
                if (outcome != GrainStateWriteOutcome.Committed)
                {
                    throw new InvalidOperationException(
                        $"Projection-fault version transition did not commit (outcome: {outcome}).");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    MultiProjectionLogEvents.ProjectionFaultVersionClearFailed,
                    ex,
                    "Projection-fault version transition failed before activation could serve: {ProjectorName}, PersistedProjectorVersion: {PersistedProjectorVersion}, RunningProjectorVersion: {RunningProjectorVersion}",
                    GetProjectorName(),
                    persistedProjectorVersion,
                    runningProjectorVersion);
                throw new InvalidOperationException(
                    $"Projection-fault version transition failed for projector '{GetProjectorName()}' from persisted version "
                    + $"'{persistedProjectorVersion ?? "(none)"}' to running version '{runningProjectorVersion}'.",
                    ex);
            }

            _logger.LogInformation(
                MultiProjectionLogEvents.ProjectionFaultVersionCleared,
                "Projection-fault version transition durably cleared the persisted fault: {ProjectorName}, PersistedProjectorVersion: {PersistedProjectorVersion}, RunningProjectorVersion: {RunningProjectorVersion}",
                GetProjectorName(),
                persistedProjectorVersion,
                runningProjectorVersion);
            return;
        }

        if (!Guid.TryParse(state.FaultEventId, out var eventId))
        {
            return;
        }

        var fault = new ProjectionFaultDescriptor(
            eventId,
            state.FaultEventType ?? string.Empty,
            GetProjectorName(),
            state.FaultPosition ?? string.Empty,
            state.FaultMessage ?? string.Empty,
            state.FaultedAtUtcTicks);

        _projectionFault = fault;
        _host.RestoreFault(fault);
    }

    private async Task HandleCatchUpBatchFailureAsync(Exception ex, string projectorName)
    {
        await CaptureAndPersistProjectionFaultIfAnyAsync();
        _catchUpConsecutiveFailureCount++;
        _catchUpFailureWindowStartUtc ??= DateTime.UtcNow;

        var failureElapsed = DateTime.UtcNow - _catchUpFailureWindowStartUtc.Value;
        var shouldAbort =
            _catchUpConsecutiveFailureCount >= _catchUpMaxConsecutiveFailures ||
            failureElapsed >= _catchUpMaxFailureDuration;

        _lastError = $"Catch-up batch failed: {ex.Message}";
        _logger.LogError(
            ex,
            "[{ProjectorName}] Catch-up batch error. BatchNumber={BatchNumber}, CurrentPosition={CurrentPosition}, TargetPosition={TargetPosition}, PendingStreamEvents={PendingStreamEvents}, FailureCount={FailureCount}, FailureElapsedSeconds={FailureElapsedSeconds:F1}",
            projectorName,
            _catchUpProgress.BatchesProcessed + 1,
            _catchUpProgress.CurrentPosition?.Value ?? "beginning",
            _catchUpProgress.TargetPosition?.Value ?? "unknown",
            _pendingStreamEvents.Count,
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
            var currentVersion = _stateStore.Committed.ProjectorVersion;
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

                    var saveResult = await UpsertExternalStateCoordinatedAsync(
                        writeRequest,
                        targetStream,
                        _injectedActorOptions?.MaxSnapshotSerializedSizeBytes ?? 2 * 1024 * 1024);
                    // A fault-block returns a ResultBox.Error, so updated stays false and the MetadataMaintenance
                    // ProjectorVersion write below is skipped: no projector-version mutation may happen after rejection.
                    if (saveResult.IsSuccess)
                    {
                        updated = true;
                    }
                }
            }

            // Update Orleans ProjectorVersion field
            if (updated)
            {
                var version = newVersion;
                await _stateStore.ExecuteWriteAsync(GrainStateWriteKind.MetadataMaintenance, s => s.ProjectorVersion = version);
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
        // SEK-G20: admin delete is routed through the SAME capability-aware invalidation as a retrograde rebuild — on a
        // capable shared store it is a durable bump+tombstone (a stale peer cannot recreate/re-contaminate outside the
        // tombstone protocol), NOT an unconditional DeleteAsync. Non-capable stores keep the legacy hard delete. Always
        // through the coordinator, so it waits for any parked/in-flight upsert and never runs concurrently with one.
        var ok = true;
        await _externalStore.InvalidateAsync(async () =>
        {
            try
            {
                await PerformCheckpointInvalidationCoreAsync();
            }
            catch
            {
                ok = false;
                throw;
            }
        });
        return ok;
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
        if (_restoreRetirementFailed)
        {
            return;
        }

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

            // Both background and first-query paths acquire START from the same one-shot resolver. The restored record
            // wins over host-payload inference and is consumed exactly once.
            var startLease = await _catchUpStartPositions.AcquireAsync(forceFull, GetCurrentPositionAsync);
            var currentPosition = startLease.StartPosition;
            MarkProjectionStatusDirty(currentPosition?.Value);

            // NOTE: We intentionally skip reading all events to determine target position.
            // Reading 200k+ events just to find the latest position causes activation timeout.
            // Instead, we start catch-up immediately and let it run until no new events are found.
            // The target position will be updated dynamically during catch-up batches.

            // Initialize catch-up progress (TargetPosition will be set during first batch)
            _catchUpProgress = new CatchUpProgress
            {
                StartLease = startLease,
                InitialPosition = currentPosition,
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
            ResetCatchUpPersistWindow(resetReadPath: true);
            _catchUpBatchSkipCount = 0;

            MoveBufferedStreamEventsToPending(currentPosition);

            _logger.LogInformation(
                MultiProjectionLogEvents.CatchUpStarted,
                "[{ProjectorName}] Starting catch-up. StartPosition={StartPosition}, CurrentPosition={CurrentPosition}, PendingStreamEvents={PendingStreamEvents}, RequestedBatchSize={RequestedBatchSize}, ServiceId={ServiceId}",
                projectorName,
                currentPosition?.Value ?? "beginning",
                currentPosition?.Value ?? "beginning",
                _pendingStreamEvents.Count,
                _catchUpBatchSize,
                _serviceId);

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
        var scheduledRun = _catchUpProgress;
        if (!scheduledRun.IsActive)
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
            _catchUpBatchSkipCount++;
            _logger.LogInformation(
                MultiProjectionLogEvents.CatchUpBatchSkipped,
                "[{ProjectorName}] Catch-up batch skipped due to global catch-up concurrency limit. NextBatchNumber={NextBatchNumber}, SkipCount={SkipCount}, WaitElapsedMs={WaitElapsedMs}, CurrentPosition={CurrentPosition}, TargetPosition={TargetPosition}, PendingStreamEvents={PendingStreamEvents}",
                projectorName,
                _catchUpProgress.BatchesProcessed + 1,
                _catchUpBatchSkipCount,
                0,
                _catchUpProgress.CurrentPosition?.Value ?? "beginning",
                _catchUpProgress.TargetPosition?.Value ?? "unknown",
                _pendingStreamEvents.Count);
            return;
        }

        try
        {
            await CatchUpProductionTestHooks.PublishAsync(
                CatchUpProductionHookPoint.BackgroundBeforeGate,
                new CatchUpProductionObservation(_serviceId, projectorName, scheduledRun.StartLease, scheduledRun.CurrentPosition));
            await using var runLease = await _catchUpExecutionGate.EnterAsync();
            await CatchUpProductionTestHooks.PublishAsync(
                CatchUpProductionHookPoint.BackgroundEnteredGate,
                new CatchUpProductionObservation(_serviceId, projectorName, scheduledRun.StartLease, scheduledRun.CurrentPosition));
            if (!ReferenceEquals(_catchUpProgress, scheduledRun) || !scheduledRun.IsActive)
            {
                await CatchUpProductionTestHooks.PublishAsync(
                    CatchUpProductionHookPoint.BackgroundRejectedAsSuperseded,
                    new CatchUpProductionObservation(_serviceId, projectorName, scheduledRun.StartLease, scheduledRun.CurrentPosition));
                return;
            }

            // Process one batch
            var batch = await ProcessSingleCatchUpBatch();
            ResetCatchUpFailureTracking();

            if (batch.FetchedCount == 0)
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
            await HandleCatchUpBatchFailureAsync(ex, projectorName);
        }
        finally
        {
            CatchUpBatchSemaphore.Release();
        }
    }

    private async Task<CatchUpBatchResult> ProcessSingleCatchUpBatch()
    {
        if (_host == null) return new CatchUpBatchResult(0, 0, null, null);
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
    private async Task<CatchUpBatchResult> ProcessSerializableBatch()
    {
        var projectorName = GetProjectorName();
        var catchUpStore = GetCatchUpEventStore();
        var hybridCatchUpStore = catchUpStore as HybridEventStore;
        var isHybridCatchUp = hybridCatchUpStore is not null;
        var batchSize = ResolveCatchUpBatchSize(hybridCatchUpStore);
        var startPosition = _catchUpProgress.CurrentPosition?.Value ?? "beginning";
        var pendingStreamEventsBefore = _pendingStreamEvents.Count;
        var batchNumber = _catchUpProgress.BatchesProcessed + 1;

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

        _logger.LogDebug(
            "[{ProjectorName}] Catch-up batch starting. BatchNumber={BatchNumber}, StartPosition={StartPosition}, CurrentPosition={CurrentPosition}, TargetPosition={TargetPosition}, RequestedMaxCount={RequestedMaxCount}, PendingStreamEventsBefore={PendingStreamEventsBefore}",
            projectorName,
            batchNumber,
            startPosition,
            _catchUpProgress.CurrentPosition?.Value ?? "beginning",
            _catchUpProgress.TargetPosition?.Value ?? "unknown",
            batchSize,
            pendingStreamEventsBefore);

        ResultBox<IEnumerable<SerializableEvent>> eventsResult;
        HybridReadBatchMetadata? hybridReadBatchMetadata = null;
        var readStopwatch = System.Diagnostics.Stopwatch.StartNew();
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
        finally
        {
            readStopwatch.Stop();
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
            // The background catch-up path is deliberately resilient (return an empty batch and retry later), but the
            // failure must not be lost: the first-query barrier reads this to fail closed with the original exception
            // rather than treat a failed read as "caught up to an empty tail".
            _catchUpReadException = exception;
            return new CatchUpBatchResult(0, 0, null, null);
        }

        _logger.LogDebug(
            "[{ProjectorName}] Catch-up using serializable path (cold/hot merge)",
            projectorName);

        var eventsEnumerable = eventsResult.GetValue();
        var events = eventsEnumerable as IReadOnlyList<SerializableEvent> ?? eventsEnumerable.ToList();
        if (events.Count == 0)
        {
            LogCatchUpBatchSummary(
                projectorName,
                new CatchUpBatchTelemetry(
                    BatchNumber: batchNumber,
                    StartPosition: startPosition,
                    CurrentPosition: _catchUpProgress.CurrentPosition?.Value ?? "beginning",
                    LastAppliedPosition: EmptyLogValue,
                    TargetPosition: _catchUpProgress.TargetPosition?.Value ?? "unknown",
                    RequestedMaxCount: batchSize,
                    FetchedCount: 0,
                    FilteredCount: 0,
                    AppliedCount: 0,
                    PendingStreamEventsBefore: pendingStreamEventsBefore,
                    PendingStreamEventsAfter: _pendingStreamEvents.Count,
                    ReadElapsedMs: readStopwatch.ElapsedMilliseconds,
                    ApplyElapsedMs: 0,
                    PersistElapsedMs: 0,
                    SafePromotionElapsedMs: 0,
                    TotalElapsedMs: readStopwatch.ElapsedMilliseconds,
                    ReadSource: hybridReadBatchMetadata?.Source ?? (isHybridCatchUp ? "hybrid_no_result" : catchUpStore.GetType().Name),
                    ColdEventsRead: hybridReadBatchMetadata?.ColdEventsRead ?? 0,
                    HotEventsRead: hybridReadBatchMetadata?.HotEventsRead ?? 0,
                    ReachedColdSegmentBoundary: hybridReadBatchMetadata?.ReachedColdSegmentBoundary ?? false,
                    SegmentCount: hybridReadBatchMetadata?.SegmentCount ?? 0,
                    PersistTriggered: false,
                    PersistReason: "none",
                    EventTypeSummary: EmptyLogValue));
            return new CatchUpBatchResult(0, 0, _catchUpProgress.CurrentPosition, null);
        }

        UpdateTargetPosition(events[^1].SortableUniqueIdValue);
        // The registry cursor is the last event fetched, including events filtered out before projector application.
        // It is intentionally distinct from the applied-event count and checkpoint cursor.
        MarkProjectionStatusDirty(events[^1].SortableUniqueIdValue);

        var filtered = FilterByPositionAndProcessed(events, e => e.Id, e => e.SortableUniqueIdValue);
        var filteredCount = Math.Max(0, events.Count - filtered.Count);
        if (filtered.Count == 0)
        {
            await UpdateCatchUpProgressAfterBatch(
                batchNumber,
                startPosition,
                Array.Empty<Guid>(),
                events[^1].SortableUniqueIdValue,
                null,
                batchSize,
                fetchedCount: events.Count,
                filteredCount,
                appliedCount: 0,
                readElapsedMs: readStopwatch.ElapsedMilliseconds,
                applyElapsedMs: 0,
                pendingStreamEventsBefore,
                hybridCatchUpStore,
                hybridReadBatchMetadata,
                BuildCatchUpEventTypeSummary(events.Select(e => e.EventPayloadName)));
            return new CatchUpBatchResult(
                events.Count,
                0,
                new SortableUniqueId(events[^1].SortableUniqueIdValue),
                null);
        }

        var applyStopwatch = System.Diagnostics.Stopwatch.StartNew();
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
        finally
        {
            applyStopwatch.Stop();
        }

        await UpdateCatchUpProgressAfterBatch(
            batchNumber,
            startPosition,
            filtered.Select(e => e.Id),
            events[^1].SortableUniqueIdValue,
            filtered[^1].SortableUniqueIdValue,
            batchSize,
            fetchedCount: events.Count,
            filteredCount,
            appliedCount: filtered.Count,
            readElapsedMs: readStopwatch.ElapsedMilliseconds,
            applyElapsedMs: applyStopwatch.ElapsedMilliseconds,
            pendingStreamEventsBefore,
            hybridCatchUpStore,
            hybridReadBatchMetadata,
            BuildCatchUpEventTypeSummary(filtered.Select(e => e.EventPayloadName)));

        return new CatchUpBatchResult(
            events.Count,
            filtered.Count,
            new SortableUniqueId(events[^1].SortableUniqueIdValue),
            new SortableUniqueId(filtered[^1].SortableUniqueIdValue));
    }

    private async Task<CatchUpBatchResult> ProcessStreamingSerializableBatch(
        IStreamingSerializableEventStore streamingCatchUpStore,
        HybridEventStore? hybridCatchUpStore,
        bool isHybridCatchUp,
        int batchSize,
        string startPosition,
        string projectorName)
    {
        if (_host == null)
        {
            return new CatchUpBatchResult(0, 0, null, null);
        }

        var processedIds = new List<Guid>(Math.Min(batchSize, StreamingCatchUpApplyChunkSize * 2));
        var buffer = new List<SerializableEvent>(Math.Min(batchSize, StreamingCatchUpApplyChunkSize));
        string? lastFetchedSortableUniqueId = null;
        string? lastProcessedSortableUniqueId = null;
        var fetchedCount = 0;
        var appliedCount = 0;
        long applyElapsedMs = 0;
        HybridReadBatchMetadata? hybridReadBatchMetadata = null;
        var pendingStreamEventsBefore = _pendingStreamEvents.Count;
        var batchNumber = _catchUpProgress.BatchesProcessed + 1;
        var eventTypeNames = new List<string>(Math.Min(batchSize, StreamingCatchUpApplyChunkSize));

        _logger.LogDebug(
            "[{ProjectorName}] Catch-up batch starting. BatchNumber={BatchNumber}, StartPosition={StartPosition}, CurrentPosition={CurrentPosition}, TargetPosition={TargetPosition}, RequestedMaxCount={RequestedMaxCount}, PendingStreamEventsBefore={PendingStreamEventsBefore}",
            projectorName,
            batchNumber,
            startPosition,
            _catchUpProgress.CurrentPosition?.Value ?? "beginning",
            _catchUpProgress.TargetPosition?.Value ?? "unknown",
            batchSize,
            pendingStreamEventsBefore);

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

            appliedCount += buffer.Count;
            lastProcessedSortableUniqueId = buffer[^1].SortableUniqueIdValue;
            buffer.Clear();
        }

        try
        {
            var readStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            using var projectionContext = HybridReadProjectionContext.Push(projectorName);
            var streamResult = await streamingCatchUpStore.StreamAllSerializableEventsAsync(
                _catchUpProgress.CurrentPosition,
                batchSize,
                async ev =>
                {
                    fetchedCount++;
                    lastFetchedSortableUniqueId = ev.SortableUniqueIdValue;
                    UpdateTargetPosition(ev.SortableUniqueIdValue);
                    MarkProjectionStatusDirty(ev.SortableUniqueIdValue);

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
                    eventTypeNames.Add(ev.EventPayloadName);
                    if (buffer.Count >= StreamingCatchUpApplyChunkSize)
                    {
                        await FlushBufferAsync();
                    }
                });
            hybridReadBatchMetadata = HybridReadProjectionContext.BatchMetadata;
            var streamElapsedMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(readStartedAt).TotalMilliseconds;
            var readElapsedMs = Math.Max(0, streamElapsedMs - applyElapsedMs);

            if (!streamResult.IsSuccess)
            {
                var exception = streamResult.GetException();
                _logger.LogError(
                    exception,
                    "[{ProjectorName}] Failed to stream serializable events for catch-up",
                    projectorName);
                // Match the enumerable path: the resilient background reader may retry later, but a first-query
                // invocation must retain and rethrow this exact provider exception when its cursor did not reach head.
                _catchUpReadException = exception;
                return new CatchUpBatchResult(0, 0, null, null);
            }
            if (fetchedCount == 0)
            {
                LogCatchUpBatchSummary(
                    projectorName,
                    new CatchUpBatchTelemetry(
                        BatchNumber: batchNumber,
                        StartPosition: startPosition,
                        CurrentPosition: _catchUpProgress.CurrentPosition?.Value ?? "beginning",
                        LastAppliedPosition: EmptyLogValue,
                        TargetPosition: _catchUpProgress.TargetPosition?.Value ?? "unknown",
                        RequestedMaxCount: batchSize,
                        FetchedCount: 0,
                        FilteredCount: 0,
                        AppliedCount: 0,
                        PendingStreamEventsBefore: pendingStreamEventsBefore,
                        PendingStreamEventsAfter: _pendingStreamEvents.Count,
                        ReadElapsedMs: readElapsedMs,
                        ApplyElapsedMs: applyElapsedMs,
                        PersistElapsedMs: 0,
                        SafePromotionElapsedMs: 0,
                        TotalElapsedMs: readElapsedMs + applyElapsedMs,
                        ReadSource: hybridReadBatchMetadata?.Source ?? (isHybridCatchUp ? "hybrid_no_result" : streamingCatchUpStore.GetType().Name),
                        ColdEventsRead: hybridReadBatchMetadata?.ColdEventsRead ?? 0,
                        HotEventsRead: hybridReadBatchMetadata?.HotEventsRead ?? 0,
                        ReachedColdSegmentBoundary: hybridReadBatchMetadata?.ReachedColdSegmentBoundary ?? false,
                        SegmentCount: hybridReadBatchMetadata?.SegmentCount ?? 0,
                        PersistTriggered: false,
                        PersistReason: "none",
                        EventTypeSummary: EmptyLogValue));
                return new CatchUpBatchResult(0, 0, _catchUpProgress.CurrentPosition, null);
            }

            await FlushBufferAsync();
            var filteredCount = Math.Max(0, fetchedCount - appliedCount);
            if (appliedCount == 0)
            {
                await UpdateCatchUpProgressAfterBatch(
                    batchNumber,
                    startPosition,
                    Array.Empty<Guid>(),
                    lastFetchedSortableUniqueId!,
                    null,
                    batchSize,
                    fetchedCount,
                    filteredCount,
                    appliedCount: 0,
                    readElapsedMs,
                    applyElapsedMs,
                    pendingStreamEventsBefore,
                    hybridCatchUpStore,
                    hybridReadBatchMetadata,
                    BuildCatchUpEventTypeSummary(eventTypeNames));
                return new CatchUpBatchResult(
                    fetchedCount,
                    0,
                    new SortableUniqueId(lastFetchedSortableUniqueId!),
                    null);
            }

            await UpdateCatchUpProgressAfterBatch(
                batchNumber,
                startPosition,
                processedIds,
                lastFetchedSortableUniqueId!,
                lastProcessedSortableUniqueId,
                batchSize,
                fetchedCount,
                filteredCount,
                appliedCount,
                readElapsedMs,
                applyElapsedMs,
                pendingStreamEventsBefore,
                hybridCatchUpStore,
                hybridReadBatchMetadata,
                BuildCatchUpEventTypeSummary(eventTypeNames));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Unknown event type", StringComparison.Ordinal))
        {
            _logger.LogError(
                ex,
                "[{ProjectorName}] Serializable catch-up failed due to unknown event type",
                projectorName);
            throw;
        }

        return new CatchUpBatchResult(
            fetchedCount,
            appliedCount,
            lastFetchedSortableUniqueId is null
                ? _catchUpProgress.CurrentPosition
                : new SortableUniqueId(lastFetchedSortableUniqueId),
            lastProcessedSortableUniqueId is null
                ? null
                : new SortableUniqueId(lastProcessedSortableUniqueId));
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
        int batchNumber,
        string startPosition,
        IEnumerable<Guid> processedIds,
        string lastFetchedSortableUniqueIdValue,
        string? lastAppliedSortableUniqueIdValue,
        int requestedMaxCount,
        int fetchedCount,
        int filteredCount,
        int appliedCount,
        long readElapsedMs,
        long applyElapsedMs,
        int pendingStreamEventsBefore,
        HybridEventStore? hybridCatchUpStore,
        HybridReadBatchMetadata? hybridReadBatchMetadata,
        string eventTypeSummary)
    {
        var projectorName = GetProjectorName();

        ObserveCatchUpReadPath(hybridReadBatchMetadata);
        _catchUpProgress.BatchesProcessed++;
        _catchUpProgress.HadNewEvents |= appliedCount > 0;
        _eventsProcessed += appliedCount;
        _eventsProcessedSinceLastCatchUpPersist += appliedCount;
        _eventsFetchedSinceLastCatchUpPersist += fetchedCount;
        _lastHybridReadBatchMetadata = hybridReadBatchMetadata;

        foreach (var id in processedIds)
        {
            TrackProcessedEventId(id);
        }

        _catchUpProgress.CurrentPosition = new SortableUniqueId(lastFetchedSortableUniqueIdValue);
        MarkProjectionStatusDirty(lastFetchedSortableUniqueIdValue, lastAppliedSortableUniqueIdValue);

        var persistDecision = GetCatchUpPersistDecision(hybridCatchUpStore, hybridReadBatchMetadata);
        long persistElapsedMs = 0;
        var persistOutcome = PersistOutcomeNotAttempted;
        if (persistDecision.ShouldPersist)
        {
            var persistStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await PersistStateAsync();
                persistOutcome = _lastPersistOutcome;
            }
            finally
            {
                persistStopwatch.Stop();
                persistElapsedMs = persistStopwatch.ElapsedMilliseconds;
                // A completed persist attempt, including a short-circuit or failed ResultBox, closes this logical
                // threshold window. Applied, fetched, and time tracking must reset as one unit.
                ResetCatchUpPersistWindow();
            }
        }

        var totalElapsedMs = readElapsedMs + applyElapsedMs + persistElapsedMs;
        LogCatchUpBatchSummary(
            projectorName,
            new CatchUpBatchTelemetry(
                BatchNumber: batchNumber,
                StartPosition: startPosition,
                CurrentPosition: _catchUpProgress.CurrentPosition?.Value ?? lastFetchedSortableUniqueIdValue,
                LastAppliedPosition: lastAppliedSortableUniqueIdValue ?? EmptyLogValue,
                TargetPosition: _catchUpProgress.TargetPosition?.Value ?? lastFetchedSortableUniqueIdValue,
                RequestedMaxCount: requestedMaxCount,
                FetchedCount: fetchedCount,
                FilteredCount: filteredCount,
                AppliedCount: appliedCount,
                PendingStreamEventsBefore: pendingStreamEventsBefore,
                PendingStreamEventsAfter: _pendingStreamEvents.Count,
                ReadElapsedMs: readElapsedMs,
                ApplyElapsedMs: applyElapsedMs,
                PersistElapsedMs: persistElapsedMs,
                SafePromotionElapsedMs: 0,
                TotalElapsedMs: totalElapsedMs,
                ReadSource: hybridReadBatchMetadata?.Source ?? (hybridCatchUpStore is not null ? "hybrid_unknown" : GetCatchUpEventStore().GetType().Name),
                ColdEventsRead: hybridReadBatchMetadata?.ColdEventsRead ?? 0,
                HotEventsRead: hybridReadBatchMetadata?.HotEventsRead ?? 0,
                ReachedColdSegmentBoundary: hybridReadBatchMetadata?.ReachedColdSegmentBoundary ?? false,
                SegmentCount: hybridReadBatchMetadata?.SegmentCount ?? 0,
                PersistTriggered: persistDecision.ShouldPersist,
                PersistReason: persistDecision.Reason,
                EventTypeSummary: eventTypeSummary)
            {
                PersistOutcome = persistOutcome
            });

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

    private CatchUpPersistDecision GetCatchUpPersistDecision(
        HybridEventStore? hybridCatchUpStore,
        HybridReadBatchMetadata? hybridReadBatchMetadata)
    {
        if (hybridReadBatchMetadata?.UsedCold == true)
        {
            if (hybridCatchUpStore is null)
            {
                return new CatchUpPersistDecision(false, "none");
            }

            if (hybridCatchUpStore.ShouldPersistSnapshotOnColdSegmentBoundary()
                && hybridReadBatchMetadata.ReachedColdSegmentBoundary)
            {
                return new CatchUpPersistDecision(true, "cold_segment_boundary");
            }

            if (_eventsProcessedSinceLastCatchUpPersist >= hybridCatchUpStore.GetCatchUpPersistMaxEventsWithoutSnapshot())
            {
                return new CatchUpPersistDecision(true, "max_events_since_last_persist");
            }

            if (DateTime.UtcNow - _lastCatchUpPersistUtc >= hybridCatchUpStore.GetCatchUpPersistMaxInterval())
            {
                return new CatchUpPersistDecision(true, "max_interval_since_last_persist");
            }

            if (_eventsFetchedSinceLastCatchUpPersist >= hybridCatchUpStore.GetCatchUpPersistMaxEventsWithoutSnapshot())
            {
                return new CatchUpPersistDecision(true, "fetched_count_checkpoint");
            }

            return new CatchUpPersistDecision(false, "none");
        }

        if (_eventsProcessed > 0 && _eventsProcessed % HotCatchUpPersistMaxFetchedEvents == 0)
        {
            return new CatchUpPersistDecision(true, "event_count_checkpoint");
        }

        if (_eventsFetchedSinceLastCatchUpPersist >= HotCatchUpPersistMaxFetchedEvents)
        {
            return new CatchUpPersistDecision(true, "fetched_count_checkpoint");
        }

        if (DateTime.UtcNow - _lastCatchUpPersistUtc >= HotCatchUpPersistMaxInterval)
        {
            return new CatchUpPersistDecision(true, "time_checkpoint");
        }

        return new CatchUpPersistDecision(false, "none");
    }

    private async Task CompleteCatchUp()
    {
        var projectorName = GetProjectorName();
        var shouldPersist = _catchUpProgress.HadNewEvents;
        var pendingStreamEventsBefore = _pendingStreamEvents.Count;
        long safePromotionElapsedMs = 0;
        long persistElapsedMs = 0;

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
                var safePromotionStopwatch = System.Diagnostics.Stopwatch.StartNew();
                await TriggerSafePromotion();
                safePromotionStopwatch.Stop();
                safePromotionElapsedMs = safePromotionStopwatch.ElapsedMilliseconds;

                // Final persistence
                var persistStopwatch = System.Diagnostics.Stopwatch.StartNew();
                await PersistStateAsync();
                persistStopwatch.Stop();
                persistElapsedMs = persistStopwatch.ElapsedMilliseconds;
            }

            // Process any pending stream events
            await ProcessPendingStreamEvents();
            CompactRetainedCollections();

            _catchUpProgress.IsActive = false;

            var elapsed = DateTime.UtcNow - _catchUpProgress.StartTime;
            _logger.LogInformation(
                MultiProjectionLogEvents.CatchUpCompleted,
                "[{ProjectorName}] Catch-up completed. BatchCount={BatchCount}, EventsProcessed={EventsProcessed}, StartPosition={StartPosition}, CurrentPosition={CurrentPosition}, TargetPosition={TargetPosition}, PendingStreamEventsBefore={PendingStreamEventsBefore}, PendingStreamEventsAfter={PendingStreamEventsAfter}, SafePromotionElapsedMs={SafePromotionElapsedMs}, PersistElapsedMs={PersistElapsedMs}, TotalElapsedMs={TotalElapsedMs}, PersistReason={PersistReason}, GlobalConcurrencySkipCount={GlobalConcurrencySkipCount}",
                projectorName,
                _catchUpProgress.BatchesProcessed,
                _eventsProcessed,
                _catchUpProgress.InitialPosition?.Value ?? "beginning",
                _catchUpProgress.CurrentPosition?.Value ?? "beginning",
                _catchUpProgress.TargetPosition?.Value ?? "unknown",
                pendingStreamEventsBefore,
                _pendingStreamEvents.Count,
                safePromotionElapsedMs,
                persistElapsedMs,
                (long)elapsed.TotalMilliseconds,
                shouldPersist ? "catch_up_complete" : "none",
                _catchUpBatchSkipCount);
        }
        finally
        {
            _catchUpProgress.IsActive = false;
            ResetCatchUpPersistWindow(resetReadPath: true);
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
            _liveLastPosition = allEvents.Last().SortableUniqueIdValue;
            MarkProjectionStatusDirty(_liveLastPosition, _liveLastPosition);
        }
    }

    private async Task<SortableUniqueId?> GetCurrentPositionAsync()
    {
        if (_host == null) return null;

        // START is derived only from the safe checkpoint. Unsafe metadata is observability/served-state information,
        // not proof that every earlier durable event was traversed; using it here can skip a missing earlier event.
        // Prefer the full safe state when available, but fall back to primitive safe metadata so catch-up progress
        // tracking does not depend on payload deserialization succeeding.
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

                // SEK-G18: live events can arrive out of global order; rebuild from the authoritative store if signaled.
                if (_host is IRebuildSignalingHost { RebuildRequired: true })
                {
                    await TriggerDurableFullRebuildAsync();
                    return;
                }
            }

            // Update position to the maximum SortableUniqueId in the batch (monotonic)
            var maxSortableId = events
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
                .Last()
                .SortableUniqueIdValue;
            _liveLastPosition = maxSortableId;
            MarkProjectionStatusDirty(maxSortableId, newEvents.Count > 0 ? maxSortableId : null);

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
            await CaptureAndPersistProjectionFaultIfAnyAsync();
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

            // SEK-G18: live events can arrive out of global order; rebuild from the authoritative store if signaled.
            if (_host is IRebuildSignalingHost { RebuildRequired: true })
            {
                await TriggerDurableFullRebuildAsync();
                return;
            }

            // Update position
            var maxSortableId = events
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
                .Last()
                .SortableUniqueIdValue;
            _liveLastPosition = maxSortableId;
            MarkProjectionStatusDirty(maxSortableId, maxSortableId);

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
            await CaptureAndPersistProjectionFaultIfAnyAsync();
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
        await CatchUpProductionTestHooks.PublishAsync(
            CatchUpProductionHookPoint.ActivationLifecycleStarted,
            new CatchUpProductionObservation(_serviceId, projectorName, null, null));
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
