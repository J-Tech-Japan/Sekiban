using Orleans.Streams;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Simplified pure infrastructure grain with minimal business logic
///     Demonstrates separation of concerns
/// </summary>
public class MultiProjectionGrain : Grain, IMultiProjectionGrain, ILifecycleParticipant<IGrainLifecycle>
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly IPersistentState<MultiProjectionGrainState> _state;
    private readonly IEventSubscriptionResolver _subscriptionResolver;
    private readonly IBlobStorageSnapshotAccessor? _snapshotAccessor;
    private readonly GeneralMultiProjectionActorOptions? _injectedActorOptions;
    
    // Orleans infrastructure
    private IAsyncStream<Event>? _orleansStream;
    private StreamSubscriptionHandle<Event>? _orleansStreamHandle;
    private IDisposable? _persistTimer;
    private IDisposable? _fallbackTimer;
    
    // Core projection actor - contains business logic
    private GeneralMultiProjectionActor? _projectionActor;
    
    // Simple tracking
    private bool _isInitialized;
    private bool _avoidOverlapOnce;
    private string? _lastError;
    private long _eventsProcessed;
    private HashSet<string> _processedEventIds = new(); // Track processed event IDs to prevent double counting
    private DateTime? _lastEventTime;
    
    // Event delivery statistics (debug/no-op selectable)
    private readonly Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics _eventStats;
    
    // Event batching
    private readonly List<Event> _eventBuffer = new();
    private readonly HashSet<string> _unsafeEventIds = new(); // Track which buffered events are unsafe
    private DateTime _lastBufferFlush = DateTime.UtcNow;
    private readonly int _batchSize = 50; // Process events in batches of 50
    private readonly TimeSpan _batchTimeout = TimeSpan.FromMilliseconds(50); // Flush promptly but return quickly to stream
    private IDisposable? _batchTimer;
    private IDisposable? _immediateFlushTimer;
    private bool _subscriptionStarting;
    private bool _catchUpRunning;
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
    private readonly Queue<Event> _pendingStreamEvents = new();
    private const int CatchUpBatchSize = 3000; // Optimized batch size after fixing O(n²) issue
    private const int MaxConsecutiveEmptyBatches = 5; // More batches before considering complete
    private readonly TimeSpan _catchUpInterval = TimeSpan.FromSeconds(1); // Standard interval after performance fix

    // Delegate these to configuration
    private readonly int _persistBatchSize = 1000; // Persist less frequently to avoid blocking deliveries
    private readonly TimeSpan _persistInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _fallbackCheckInterval = TimeSpan.FromSeconds(30);

    public MultiProjectionGrain(
        [PersistentState("multiProjection", "OrleansStorage")] IPersistentState<MultiProjectionGrainState> state,
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IEventSubscriptionResolver? subscriptionResolver,
        IBlobStorageSnapshotAccessor? snapshotAccessor,
        Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics? eventStats,
        GeneralMultiProjectionActorOptions? actorOptions)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _subscriptionResolver = subscriptionResolver ?? new DefaultOrleansEventSubscriptionResolver();
        _snapshotAccessor = snapshotAccessor;
        _eventStats = eventStats ?? new Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics();
        _injectedActorOptions = actorOptions;
    }

    public async Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();
        
        if (_projectionActor == null)
        {
            return ResultBox.Error<MultiProjectionState>(
                new InvalidOperationException("Projection actor not initialized"));
        }
        await StartSubscriptionAsync();
        await CatchUpFromEventStoreAsync();
        return await _projectionActor.GetStateAsync(canGetUnsafeState);
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

    public async Task<ResultBox<string>> GetSnapshotJsonAsync(bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();

        if (_projectionActor == null)
        {
            return ResultBox.Error<string>(
                new InvalidOperationException("Projection actor not initialized"));
        }

        var rb = await _projectionActor.GetSnapshotAsync(canGetUnsafeState);
        if (!rb.IsSuccess)
            return ResultBox.Error<string>(rb.GetException());
        var json = JsonSerializer.Serialize(rb.GetValue(), _domainTypes.JsonSerializerOptions);
        return ResultBox.FromValue(json);
    }

    public async Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true)
    {
        await EnsureInitializedAsync();

        if (_projectionActor == null)
        {
            throw new InvalidOperationException("Projection actor not initialized");
        }

        // Track event deliveries as well for events coming from the EventStore catch-up
        // so that delivery statistics include both stream and catch-up paths.
        _eventStats.RecordCatchUpBatch(events);

        // Filter out already processed events to prevent double counting
        var newEvents = events.Where(e => !_processedEventIds.Contains(e.Id.ToString())).ToList();
        
        if (newEvents.Count > 0)
        {
            // Delegate to projection actor
            await _projectionActor.AddEventsAsync(newEvents, finishedCatchUp, Sekiban.Dcb.Actors.EventSource.CatchUp);
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
        if (_projectionActor != null)
        {
            try
            {
                // Compute both safe and unsafe sizes from in-memory state (payload only, with runtime type)
                var unsafeStateResult = await _projectionActor.GetStateAsync(canGetUnsafeState: true);
                var safeStateResult = await _projectionActor.GetStateAsync(canGetUnsafeState: false);

                if (unsafeStateResult.IsSuccess)
                {
                    var us = unsafeStateResult.GetValue();
                    unsafeStateSize = EstimatePayloadJsonSize(us.Payload, includeUnsafeDetails: true);
                }

                if (safeStateResult.IsSuccess)
                {
                    var ss = safeStateResult.GetValue();
                    safeStateSize = EstimatePayloadJsonSize(ss.Payload, includeUnsafeDetails: false);
                }

                stateSize = safeStateSize; // Backward-compatible: report safe payload size in StateSize
                var projectorName = this.GetPrimaryKeyString();
                Console.WriteLine($"[{projectorName}] State size - Safe: {safeStateSize:N0} bytes, Unsafe: {unsafeStateSize:N0} bytes, Events: {_eventsProcessed:N0}");
            }
            catch
            {
                // Ignore errors when estimating size during status fetch
            }
        }

        return new MultiProjectionGrainStatus(
            this.GetPrimaryKeyString(),
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

    private long EstimatePayloadJsonSize(object payload, bool includeUnsafeDetails)
    {
        try
        {
            // Special handling for TagState-based projectors (payloads exposing a 'State' property
            // of type SafeUnsafeProjectionState<,>), where direct JSON would be minimal due to
            // private fields. Build a representative DTO to reflect collection sizes.
            var payloadType = payload.GetType();
            var stateProp = payloadType.GetProperty("State");
            if (stateProp != null)
            {
                var stateObj = stateProp.GetValue(payload);
                if (stateObj != null && stateObj.GetType().Name.StartsWith("SafeUnsafeProjectionState"))
                {
                    var stateType = stateObj.GetType();
                    var currentDataField = stateType.GetField("_currentData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var safeBackupField = stateType.GetField("_safeBackup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                    var currentData = currentDataField?.GetValue(stateObj) as System.Collections.IDictionary;
                    var safeBackup = safeBackupField?.GetValue(stateObj) as System.Collections.IDictionary;

                    var safeKeys = new List<string>();
                    var unsafeKeys = new List<string>();
                    var unsafeEventIds = new List<string>();

                    if (currentData != null)
                    {
                        foreach (System.Collections.DictionaryEntry de in currentData)
                        {
                            var keyStr = de.Key?.ToString() ?? string.Empty;
                            safeKeys.Add(keyStr);
                        }
                    }
                    if (safeBackup != null)
                    {
                        var backupKeys = new HashSet<string>();
                        foreach (System.Collections.DictionaryEntry de in safeBackup)
                        {
                            var keyStr = de.Key?.ToString() ?? string.Empty;
                            backupKeys.Add(keyStr);
                        }
                        // Unsafe keys are those in backup
                        unsafeKeys.AddRange(backupKeys);
                        // Safe-only keys are those not in backup
                        safeKeys = safeKeys.Where(k => !backupKeys.Contains(k)).ToList();

                        if (includeUnsafeDetails)
                        {
                            foreach (System.Collections.DictionaryEntry de in safeBackup)
                            {
                                var backupVal = de.Value; // SafeStateBackup<TState>
                                var unsafeEventsProp = backupVal?.GetType().GetProperty("UnsafeEvents");
                                var list = unsafeEventsProp?.GetValue(backupVal) as System.Collections.IEnumerable;
                                if (list != null)
                                {
                                    foreach (var ev in list)
                                    {
                                        var idProp = ev.GetType().GetProperty("Id");
                                        var id = idProp?.GetValue(ev)?.ToString() ?? string.Empty;
                                        unsafeEventIds.Add(id);
                                    }
                                }
                            }
                        }
                    }

                    object dto = includeUnsafeDetails
                        ? new { safeKeys, unsafeKeys, unsafeEventIds }
                        : new { safeKeys };
                    var json = JsonSerializer.Serialize(dto, _domainTypes.JsonSerializerOptions);
                    return Encoding.UTF8.GetByteCount(json);
                }
            }

            // Default: serialize payload itself with runtime type
            var defJson = JsonSerializer.Serialize(payload, payload.GetType(), _domainTypes.JsonSerializerOptions);
            return Encoding.UTF8.GetByteCount(defJson);
        }
        catch
        {
            return 0;
        }
    }

    public async Task<ResultBox<bool>> PersistStateAsync()
    {
        try
        {
            var startUtc = DateTime.UtcNow;
            if (_projectionActor == null)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("Projection actor not initialized"));
            }
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Starting persistence at {startUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");

            // Phase1: force promotion of buffered events before snapshot
            try
            {
                var promote = _projectionActor.GetType().GetMethod("ForcePromoteBufferedEvents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                promote?.Invoke(_projectionActor, Array.Empty<object>());
            }
            catch { }

            // Ask actor to build a persistable snapshot (with size validation + offload included)
            var persistable = await _projectionActor.BuildSnapshotForPersistenceAsync(false);
            if (!persistable.IsSuccess)
            {
                _lastError = persistable.GetException().Message;
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] WARNING: {_lastError}");
                return ResultBox.FromValue(false);
            }

            var data = persistable.GetValue();
            try
            {
                var unsafeStateInfo = await _projectionActor.GetStateAsync(true);
                var safeStateInfo = await _projectionActor.GetStateAsync(false);
                if (unsafeStateInfo.IsSuccess && safeStateInfo.IsSuccess)
                {
                    var unsafeSt = unsafeStateInfo.GetValue();
                    var safeSt = safeStateInfo.GetValue();
                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] Snapshot state - Safe: {safeSt.Version} events @ {safeSt.LastSortableUniqueId?.Substring(0, 20) ?? "empty"}, Unsafe: {unsafeSt.Version} events @ {unsafeSt.LastSortableUniqueId?.Substring(0, 20) ?? "empty"}");
                }
            }
            catch { }
            var storageProviderName = "OrleansStorage"; // 現在利用しているプロバイダ名想定
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Writing snapshot: {data.Size:N0} bytes, {_eventsProcessed:N0} events, checkpoint: {data.SafeLastSortableUniqueId?.Substring(0, 20) ?? "empty"}...");

            // Update grain state
            _state.State.ProjectorName = this.GetPrimaryKeyString();
            _state.State.SerializedState = data.Json;
            _state.State.LastPosition = data.SafeLastSortableUniqueId;
            _state.State.SafeLastPosition = data.SafeLastSortableUniqueId;
            _state.State.EventsProcessed = _eventsProcessed;
            _state.State.StateSize = data.Size;
            _state.State.LastPersistTime = DateTime.UtcNow;

            await _state.WriteStateAsync();
            _lastError = null;
            var finishUtc = DateTime.UtcNow;
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ✓ Persistence completed in {(finishUtc-startUtc).TotalMilliseconds:F0}ms - {data.Size:N0} bytes, {_eventsProcessed:N0} events saved");
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _lastError = $"Persistence failed: {ex.Message}";
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ✗ Persistence failed: {ex.Message}");
            return ResultBox.Error<bool>(ex);
        }
    }

    // Debug: force promotion of ALL buffered events regardless of window
    public Task ForcePromoteAllAsync()
    {
        if (_projectionActor != null)
        {
            try
            {
                var m = _projectionActor.GetType().GetMethod("ForcePromoteAllBufferedEvents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                m?.Invoke(_projectionActor, Array.Empty<object>());
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
            var projectorName = this.GetPrimaryKeyString();
            var streamInfo = _subscriptionResolver.Resolve(projectorName);
            if (streamInfo is OrleansSekibanStream orleansStream)
            {
                var streamProvider = this.GetStreamProvider(orleansStream.ProviderName);
                _orleansStream = streamProvider.GetStream<Event>(
                    StreamId.Create(orleansStream.StreamNamespace, orleansStream.StreamId));
            }
        }

        // Subscribe to Orleans stream if not already subscribed
        if (_orleansStreamHandle == null && _orleansStream != null && !_subscriptionStarting)
        {
            try
            {
                _subscriptionStarting = true;
                var projectorName = this.GetPrimaryKeyString();
                Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Starting subscription to Orleans stream");

                var observer = new StreamBatchObserver(this);

                // Check for existing persistent subscriptions and resume/deduplicate
                var existing = await _orleansStream.GetAllSubscriptionHandles();
                if (existing != null && existing.Count > 0)
                {
                    // Resume the oldest handle
                    var primary = existing[0];
                    _orleansStreamHandle = await primary.ResumeAsync(observer);
                    Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Resumed existing stream subscription ({existing.Count} handles found)");

                    // Unsubscribe duplicates
                    for (int i = 1; i < existing.Count; i++)
                    {
                        try
                        {
                            await existing[i].UnsubscribeAsync();
                            Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Unsubscribed duplicate stream subscription handle #{i}");
                        }
                        catch (Exception exDup)
                        {
                            Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] WARNING: Failed to unsubscribe duplicate handle #{i}: {exDup.Message}");
                        }
                    }
                }
                else
                {
                    _orleansStreamHandle = await _orleansStream.SubscribeAsync(observer, null);
                    Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Successfully subscribed to Orleans stream (new)");
                }
            }
            catch (Exception ex)
            {
                var projectorName = this.GetPrimaryKeyString();
                Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] ERROR: Failed to subscribe to Orleans stream: {ex}");
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
            var projectorName = this.GetPrimaryKeyString();
            Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Stream subscription already active");
        }
        // Do not auto-catch-up here; catch-up will be triggered by state/query access
    }

    public async Task<QueryResultGeneral> ExecuteQueryAsync(IQueryCommon query)
    {
        await EnsureInitializedAsync();

        if (_projectionActor == null)
        {
            return new QueryResultGeneral(null!, string.Empty, query);
        }

        try
        {
            await StartSubscriptionAsync();
            ResultBox<MultiProjectionState>? safeStateResultBox = null;
            if (_projectionActor != null)
            {
                safeStateResultBox = await _projectionActor.GetStateAsync(canGetUnsafeState: false);
            }
            // Avoid forcing catch-up on every query. Subscription will advance state.
            // Only catch up here if subscription is not active.
            if (_orleansStreamHandle == null)
            {
                await CatchUpFromEventStoreAsync();
            }
            var stateResult = await _projectionActor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                return new QueryResultGeneral(null!, string.Empty, query);
            }

            var state = stateResult.GetValue();
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload!));
            int? safeVersion = null;
            string? safeThreshold = null;
            DateTime? safeThresholdTime = null;
            int? unsafeVersion = null;
            try
            {
                if (safeStateResultBox != null && safeStateResultBox.IsSuccess)
                {
                    safeVersion = safeStateResultBox.GetValue().Version;
                }
                else
                {
                    var payloadType = state.Payload.GetType();
                    var safeVersionProp = payloadType.GetProperty("SafeVersion");
                    if (safeVersionProp != null)
                    {
                        safeVersion = safeVersionProp.GetValue(state.Payload) as int?;
                    }
                }
                if (_projectionActor != null)
                {
                    var actorSafeThreshold = _projectionActor.PeekCurrentSafeWindowThreshold();
                    safeThreshold = actorSafeThreshold.Value;
                    try { safeThresholdTime = actorSafeThreshold.GetDateTime(); } catch { }
                }
                unsafeVersion = state.Version;
            }
            catch { }
            var result = await _domainTypes.QueryTypes.ExecuteQueryAsync(
                query, 
                projectorProvider, 
                ServiceProvider,
                safeVersion,
                safeThreshold,
                safeThresholdTime,
                unsafeVersion);

            if (result.IsSuccess)
            {
                var value = result.GetValue();
                return new QueryResultGeneral(value, value?.GetType().FullName ?? string.Empty, query);
            }

            return new QueryResultGeneral(null!, string.Empty, query);
        }
        catch (Exception ex)
        {
            _lastError = $"Query failed: {ex.Message}";
            return new QueryResultGeneral(null!, string.Empty, query);
        }
    }

    public async Task<ListQueryResultGeneral> ExecuteListQueryAsync(IListQueryCommon query)
    {
        await EnsureInitializedAsync();

        await StartSubscriptionAsync();
        ResultBox<MultiProjectionState>? safeStateResultBox = null;
        if (_projectionActor != null)
        {
            safeStateResultBox = await _projectionActor.GetStateAsync(canGetUnsafeState: false);
        }
        if (_orleansStreamHandle == null)
        {
            await CatchUpFromEventStoreAsync();
        }

        if (_projectionActor == null)
        {
            return ListQueryResultGeneral.Empty;
        }

        try
        {
            var stateResult = await _projectionActor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                return ListQueryResultGeneral.Empty;
            }

            var state = stateResult.GetValue();
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload!));
            int? safeVersion = null;
            string? safeThreshold = null;
            DateTime? safeThresholdTime = null;
            int? unsafeVersion = null;
            try
            {
                if (safeStateResultBox != null && safeStateResultBox.IsSuccess)
                {
                    safeVersion = safeStateResultBox.GetValue().Version;
                }
                else
                {
                    var payloadType = state.Payload.GetType();
                    var safeVersionProp = payloadType.GetProperty("SafeVersion");
                    if (safeVersionProp != null)
                    {
                        safeVersion = safeVersionProp.GetValue(state.Payload) as int?;
                    }
                }
                if (_projectionActor != null)
                {
                    var actorSafeThreshold = _projectionActor.PeekCurrentSafeWindowThreshold();
                    safeThreshold = actorSafeThreshold.Value;
                    try { safeThresholdTime = actorSafeThreshold.GetDateTime(); } catch { }
                }
                unsafeVersion = state.Version;
            }
            catch { }
            var result = await _domainTypes.QueryTypes.ExecuteListQueryAsGeneralAsync(
                query,
                projectorProvider,
                ServiceProvider,
                safeVersion,
                safeThreshold,
                safeThresholdTime,
                unsafeVersion);

            return result.IsSuccess ? result.GetValue() : ListQueryResultGeneral.Empty;
        }
        catch (Exception ex)
        {
            _lastError = $"List query failed: {ex.Message}";
            return ListQueryResultGeneral.Empty;
        }
    }

    public async Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        await EnsureInitializedAsync();
        
        if (_projectionActor == null) return false;
        
        return await _projectionActor.IsSortableUniqueIdReceived(sortableUniqueId);
    }

    public async Task RefreshAsync()
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Refreshing: Re-reading events from event store");
        await CatchUpFromEventStoreAsync();
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var projectorName = this.GetPrimaryKeyString();
        Console.WriteLine($"[SimplifiedPureGrain] OnActivateAsync for {projectorName}");

        // Create projection actor
        bool forceFullCatchUp = false;
        if (_projectionActor == null)
        {
            Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Creating new projection actor");
            // Merge injected options with grain-specific snapshot accessor
            var baseOptions = _injectedActorOptions ?? new GeneralMultiProjectionActorOptions();
            var mergedOptions = new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = baseOptions.SafeWindowMs,
                SnapshotAccessor = _snapshotAccessor ?? baseOptions.SnapshotAccessor,
                SnapshotOffloadThresholdBytes = baseOptions.SnapshotOffloadThresholdBytes,
                MaxSnapshotSerializedSizeBytes = baseOptions.MaxSnapshotSerializedSizeBytes,
                EnableDynamicSafeWindow = baseOptions.EnableDynamicSafeWindow,
                MaxExtraSafeWindowMs = baseOptions.MaxExtraSafeWindowMs,
                LagEmaAlpha = baseOptions.LagEmaAlpha,
                LagDecayPerSecond = baseOptions.LagDecayPerSecond
            };

            _projectionActor = new GeneralMultiProjectionActor(
                _domainTypes,
                projectorName,
                mergedOptions);

            // Restore persisted state if available
            if (_state.State != null && !string.IsNullOrEmpty(_state.State.SerializedState))
            {
                try
                {
                    Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Restoring persisted state from storage");
                    var deserializedState = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(
                        _state.State.SerializedState,
                        _domainTypes.JsonSerializerOptions);

                    if (deserializedState != null)
                    {
                        await _projectionActor.SetSnapshotAsync(deserializedState);
                        _eventsProcessed = _state.State.EventsProcessed;
                        // Clear processed event IDs to prevent double counting after snapshot restore
                        _processedEventIds.Clear();
                        Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Snapshot restored successfully - Position: {deserializedState.InlineState?.LastSortableUniqueId ?? "(empty)"}, Events: {_eventsProcessed}");
                        // Avoid overlap on the first catch-up after snapshot restore
                        _avoidOverlapOnce = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Snapshot restore version mismatch or error: {ex.Message}. Restoring payload in compatibility mode and catching up from store.");
                    // Try compatibility restore: ignore version and use snapshot payload to ensure current values are available
                    try
                    {
                        var deserializedState = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(
                            _state.State!.SerializedState!,
                            _domainTypes.JsonSerializerOptions);
                        if (deserializedState != null)
                        {
                            SerializableMultiProjectionState reconstructed = deserializedState.IsOffloaded
                                ? new SerializableMultiProjectionState(
                                    await _snapshotAccessor!.ReadAsync(deserializedState.OffloadedState!.OffloadKey, default),
                                    deserializedState.OffloadedState!.MultiProjectionPayloadType,
                                    deserializedState.OffloadedState!.ProjectorName,
                                    deserializedState.OffloadedState!.ProjectorVersion,
                                    deserializedState.OffloadedState!.LastSortableUniqueId,
                                    deserializedState.OffloadedState!.LastEventId,
                                    deserializedState.OffloadedState!.Version,
                                    deserializedState.OffloadedState!.IsCatchedUp,
                                    deserializedState.OffloadedState!.IsSafeState)
                                : deserializedState.InlineState!;

                            await _projectionActor.SetCurrentStateIgnoringVersion(reconstructed);
                            // Avoid overlap on the first catch-up after snapshot compatibility restore
                            _avoidOverlapOnce = true;
                        }
                        // Keep persisted positions; no full replay
                        forceFullCatchUp = false;
                    }
                    catch (Exception clearEx)
                    {
                        Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Compatibility restore failed: {clearEx.Message}");
                        // As a last resort, clear snapshot and do incremental catch-up
                        try
                        {
                            _state.State!.SerializedState = null;
                            _state.State.EventsProcessed = 0;
                            await _state.WriteStateAsync();
                            forceFullCatchUp = false;
                        }
                        catch
                        {
                            forceFullCatchUp = false;
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] No persisted state to restore");
                // If positions exist in persisted grain state, resume from there; otherwise full catch-up
                var hasPersistedPos = !string.IsNullOrEmpty(_state.State?.SafeLastPosition) || !string.IsNullOrEmpty(_state.State?.LastPosition);
                forceFullCatchUp = !hasPersistedPos;
            }
        }

        await base.OnActivateAsync(cancellationToken);

        // After activation, catch up from the event store.
        // If snapshot restore failed or there was no snapshot, perform a full catch-up to rebuild current state immediately.
        await CatchUpFromEventStoreAsync(forceFullCatchUp);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[SimplifiedPureGrain-{this.GetPrimaryKeyString()}] Deactivating - Reason: {reason}");
        
        // Stop catch-up if active
        if (_catchUpProgress.IsActive)
        {
            _catchUpProgress.IsActive = false;
            _catchUpTimer?.Dispose();
            _catchUpTimer = null;
        }
        
        // Persist state before deactivation
        await PersistStateAsync();
        
        // Clean up Orleans resources
        if (_orleansStreamHandle != null)
        {
            await _orleansStreamHandle.UnsubscribeAsync();
        }

        // Flush any remaining events
        await FlushEventBufferAsync();
        
        _persistTimer?.Dispose();
        _fallbackTimer?.Dispose();
        _batchTimer?.Dispose();
        _catchUpTimer?.Dispose();

        await base.OnDeactivateAsync(reason, cancellationToken);
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
            if (string.IsNullOrEmpty(_state.State?.SerializedState))
            {
                return false;
            }

            var envelope = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(
                _state.State!.SerializedState!, _domainTypes.JsonSerializerOptions);
            if (envelope == null) return false;

            SerializableMultiProjectionStateEnvelope modified;
            if (!envelope.IsOffloaded && envelope.InlineState != null)
            {
                var s = envelope.InlineState;
                modified = new SerializableMultiProjectionStateEnvelope(
                    false,
                    new SerializableMultiProjectionState(
                        s.Payload, s.MultiProjectionPayloadType, s.ProjectorName, newVersion,
                        s.LastSortableUniqueId, s.LastEventId, s.Version, s.IsCatchedUp, s.IsSafeState),
                    null);
            }
            else if (envelope.OffloadedState != null)
            {
                var o = envelope.OffloadedState;
                modified = new SerializableMultiProjectionStateEnvelope(
                    true,
                    null,
                    new SerializableMultiProjectionStateOffloaded(
                        o.OffloadKey, o.StorageProvider, o.MultiProjectionPayloadType, o.ProjectorName,
                        newVersion, o.LastSortableUniqueId, o.LastEventId, o.Version, o.IsCatchedUp, o.IsSafeState,
                        o.PayloadLength));
            }
            else
            {
                return false;
            }

            _state.State.SerializedState = JsonSerializer.Serialize(modified, _domainTypes.JsonSerializerOptions);
            await _state.WriteStateAsync();
            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"OverwritePersistedStateVersion failed: {ex.Message}";
            return false;
        }
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
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Fallback: No stream events for over 1 minute, checking event store");
            await RefreshAsync();
        }
    }

    private async Task CatchUpFromEventStoreAsync(bool forceFull = false)
    {
        // Legacy method for compatibility - now triggers timer-based catch-up
        if (_projectionActor == null || _eventStore == null) return;
        
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
        var projectorName = this.GetPrimaryKeyString();
        
        // Get current position
        SortableUniqueId? currentPosition = await GetCurrentPositionAsync();
        
        // Get latest position in event store
        var latestResult = await _eventStore.ReadAllEventsAsync(since: null);
        if (!latestResult.IsSuccess || !latestResult.GetValue().Any())
        {
            Console.WriteLine($"[{projectorName}] No events in event store, skipping catch-up");
            return;
        }
        
        var latestEvent = latestResult.GetValue().LastOrDefault();
        if (latestEvent == null)
        {
            Console.WriteLine($"[{projectorName}] No events in event store, skipping catch-up");
            return;
        }
        var targetPosition = new SortableUniqueId(latestEvent.SortableUniqueIdValue);
        
        // Check if catch-up is needed
        if (currentPosition != null && currentPosition.Value == targetPosition.Value)
        {
            Console.WriteLine($"[{projectorName}] Already at latest position, no catch-up needed");
            return;
        }
        
        // Initialize catch-up progress
        _catchUpProgress = new CatchUpProgress
        {
            CurrentPosition = currentPosition,
            TargetPosition = targetPosition,
            IsActive = true,
            ConsecutiveEmptyBatches = 0,
            BatchesProcessed = 0,
            StartTime = DateTime.UtcNow,
            LastAttempt = DateTime.MinValue
        };
        
        Console.WriteLine($"[{projectorName}] Starting catch-up from {currentPosition?.Value ?? "beginning"} to {targetPosition.Value}");
        
        // Start catch-up timer
        StartCatchUpTimer();
    }
    
    private void StartCatchUpTimer()
    {
        if (_catchUpTimer != null)
        {
            return; // Timer already running
        }
        
        var projectorName = this.GetPrimaryKeyString();
        Console.WriteLine($"[{projectorName}] Starting catch-up timer with interval: {_catchUpInterval.TotalMilliseconds}ms");
        
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
        
        var projectorName = this.GetPrimaryKeyString();
        
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
            Console.WriteLine($"[{projectorName}] Catch-up batch error: {ex.Message}");
            // Continue with next timer execution
        }
    }
    
    private async Task<int> ProcessSingleCatchUpBatch()
    {
        if (_projectionActor == null || _eventStore == null) return 0;
        
        var projectorName = this.GetPrimaryKeyString();
        
        // Always use small batch size to avoid blocking
        var batchSize = CatchUpBatchSize;
        
        // Read batch of events
        var eventsResult = _catchUpProgress.CurrentPosition == null
            ? await _eventStore.ReadAllEventsAsync(since: null)
            : await _eventStore.ReadAllEventsAsync(since: _catchUpProgress.CurrentPosition.Value);
        
        if (!eventsResult.IsSuccess)
        {
            Console.WriteLine($"[{projectorName}] Failed to read events: {eventsResult.GetException().Message}");
            return 0;
        }
        
        var allEvents = eventsResult.GetValue().ToList();
        if (allEvents.Count == 0)
        {
            return 0;
        }
        
        // Limit batch size based on whether this is initial catch-up
        var events = allEvents.Take(batchSize).ToList();
        
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
        
        // Separate safe and unsafe events
        var safeThreshold = GetSafeWindowThreshold();
        var safeEvents = new List<Event>();
        var unsafeEvents = new List<Event>();
        
        foreach (var ev in filtered)
        {
            var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);
            if (eventTime.IsEarlierThanOrEqual(safeThreshold))
            {
                safeEvents.Add(ev);
            }
            else
            {
                unsafeEvents.Add(ev);
            }
        }
        
        // Process safe events immediately (false = outside safe window, these are "safe" to persist)
        if (safeEvents.Count > 0)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[{projectorName}] Catch-up: Processing {safeEvents.Count} safe events");
            await _projectionActor.AddEventsAsync(safeEvents, false, EventSource.CatchUp);
            sw.Stop();
            _eventsProcessed += safeEvents.Count;
            
            if (sw.ElapsedMilliseconds > 1000)
            {
                Console.WriteLine($"[{projectorName}] WARNING: AddEventsAsync took {sw.ElapsedMilliseconds}ms for {safeEvents.Count} events");
            }
            
            // Mark as processed
            foreach (var ev in safeEvents)
            {
                _processedEventIds.Add(ev.Id.ToString());
            }
        }
        
        // Buffer unsafe events for later
        if (unsafeEvents.Count > 0)
        {
            Console.WriteLine($"[{projectorName}] Catch-up: Buffering {unsafeEvents.Count} unsafe events");
            foreach (var ev in unsafeEvents)
            {
                _eventBuffer.Add(ev);
                _unsafeEventIds.Add(ev.Id.ToString()); // Mark as unsafe
                _processedEventIds.Add(ev.Id.ToString());
            }
            _eventsProcessed += unsafeEvents.Count;
        }
        
        // Update position
        var lastProcessed = filtered.Last();
        _catchUpProgress.CurrentPosition = new SortableUniqueId(lastProcessed.SortableUniqueIdValue);
        
        // Periodic persistence - only persist every 5000 events during catch-up
        if (_eventsProcessed > 0 && _eventsProcessed % 5000 == 0)
        {
            Console.WriteLine($"[{projectorName}] Persisting state at {_eventsProcessed:N0} events");
            await PersistStateAsync();
        }
        
        // Log progress only every 10 batches to reduce log spam
        if (_catchUpProgress.BatchesProcessed % 10 == 0 || filtered.Count == 0)
        {
            var elapsed = DateTime.UtcNow - _catchUpProgress.StartTime;
            var eventsPerSecond = _eventsProcessed > 0 && elapsed.TotalSeconds > 0 
                ? (_eventsProcessed / elapsed.TotalSeconds).ToString("F0")
                : "0";
            Console.WriteLine($"[{projectorName}] Catch-up: Batch #{_catchUpProgress.BatchesProcessed}, " +
                             $"{_eventsProcessed:N0} events ({eventsPerSecond}/sec), " +
                             $"elapsed: {elapsed.TotalSeconds:F1}s");
        }
        
        return filtered.Count;
    }
    
    private async Task CompleteCatchUp()
    {
        var projectorName = this.GetPrimaryKeyString();
        
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
        Console.WriteLine($"[{projectorName}] ✓ Catch-up completed: {_catchUpProgress.BatchesProcessed} batches, " +
                         $"{_eventsProcessed:N0} events, elapsed: {elapsed.TotalSeconds:F1}s");
    }
    
    private async Task TriggerSafePromotion()
    {
        try
        {
            if (_projectionActor != null)
            {
                var projectorName = this.GetPrimaryKeyString();
                Console.WriteLine($"[{projectorName}] Triggering safe promotion check after catch-up");
                
                // Get the current safe state to trigger promotion
                var safeState = await _projectionActor.GetStateAsync(canGetUnsafeState: false);
                if (safeState.IsSuccess)
                {
                    var state = safeState.GetValue();
                    Console.WriteLine($"[{projectorName}] Safe state after promotion: version={state.Version}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Error during safe promotion: {ex.Message}");
        }
    }
    
    private async Task ProcessPendingStreamEvents()
    {
        if (_pendingStreamEvents.Count == 0) return;
        
        var projectorName = this.GetPrimaryKeyString();
        var events = new List<Event>();
        
        while (_pendingStreamEvents.Count > 0)
        {
            events.Add(_pendingStreamEvents.Dequeue());
        }
        
        Console.WriteLine($"[{projectorName}] Processing {events.Count} pending stream events");
        
        // Determine safe/unsafe status for each event
        var safeThreshold = GetSafeWindowThreshold();
        var safeEvents = new List<Event>();
        var unsafeEvents = new List<Event>();
        
        foreach (var ev in events)
        {
            var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);
            if (eventTime.IsEarlierThanOrEqual(safeThreshold))
            {
                safeEvents.Add(ev);
            }
            else
            {
                unsafeEvents.Add(ev);
            }
        }
        
        // Process all events - the actor will determine safe/unsafe based on current time
        // The second parameter is finishedCatchUp, not withinSafeWindow!
        var allEvents = safeEvents.Concat(unsafeEvents).OrderBy(e => e.SortableUniqueIdValue).ToList();
        if (allEvents.Count > 0)
        {
            await _projectionActor!.AddEventsAsync(allEvents, true, EventSource.Stream);
        }
    }
    
    private async Task<SortableUniqueId?> GetCurrentPositionAsync()
    {
        if (_projectionActor == null) return null;
        
        // Try to get from current state
        var currentState = await _projectionActor.GetStateAsync(canGetUnsafeState: false);
        if (currentState.IsSuccess)
        {
            var state = currentState.GetValue();
            if (!string.IsNullOrEmpty(state.LastSortableUniqueId))
            {
                return new SortableUniqueId(state.LastSortableUniqueId);
            }
        }
        
        // Fallback to persisted state
        if (!string.IsNullOrEmpty(_state.State?.SafeLastPosition))
        {
            return new SortableUniqueId(_state.State.SafeLastPosition);
        }
        
        if (!string.IsNullOrEmpty(_state.State?.LastPosition))
        {
            return new SortableUniqueId(_state.State.LastPosition);
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

    /// <summary>
    ///     Process a batch of events from the stream
    /// </summary>
    internal async Task ProcessEventBatch(IReadOnlyList<Event> events)
    {
        if (!_isInitialized || _projectionActor == null)
        {
            await EnsureInitializedAsync();
        }

        if (_projectionActor == null || events.Count == 0) return;

        try
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Stream batch received: {events.Count} events");

            // Track event deliveries for debugging
            _eventStats.RecordStreamBatch(events);

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
                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] Buffered {buffered} stream events during catch-up (queue size: {_pendingStreamEvents.Count})");
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
                // Determine safe/unsafe for each event based on current time
                var safeThreshold = GetSafeWindowThreshold();
                var safeStreamEvents = new List<Event>();
                var unsafeStreamEvents = new List<Event>();
                
                foreach (var ev in newEvents)
                {
                    var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);
                    if (eventTime.IsEarlierThanOrEqual(safeThreshold))
                    {
                        safeStreamEvents.Add(ev);
                    }
                    else
                    {
                        unsafeStreamEvents.Add(ev);
                    }
                }
                
                // Process all events together - the actor will determine safe/unsafe based on current time
                // The second parameter is finishedCatchUp, not withinSafeWindow!
                await _projectionActor.AddEventsAsync(newEvents, true, Sekiban.Dcb.Actors.EventSource.Stream);
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

            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ✓ Processed {newEvents.Count} events - Total: {_eventsProcessed:N0} events");

            // Persist state after processing a batch if it's large enough
            if (newEvents.Count >= _persistBatchSize)
            {
                await PersistStateAsync();
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to process event batch: {ex.Message}";
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Error processing event batch: {ex}");
            
            // Log inner exception for better debugging
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Inner exception: {ex.InnerException}");
                _lastError += $" Inner: {ex.InnerException.Message}";
            }
        }
    }

    /// <summary>
    ///     Process buffered events - called by timer
    /// </summary>
    private async Task FlushEventBufferAsync()
    {
        List<Event> eventsToProcess;
        HashSet<string> unsafeIds;
        lock (_eventBuffer)
        {
            if (_eventBuffer.Count == 0)
            {
                // Even if buffer is empty, trigger safe promotion periodically
                // to ensure events transition from unsafe to safe over time
                eventsToProcess = new List<Event>();
                unsafeIds = new HashSet<string>();
            }
            else
            {
                eventsToProcess = new List<Event>(_eventBuffer);
                unsafeIds = new HashSet<string>(_unsafeEventIds);
                _eventBuffer.Clear();
                _unsafeEventIds.Clear();
                _lastBufferFlush = DateTime.UtcNow;
            }
        }
        
        if (eventsToProcess.Count > 0)
        {
            await ProcessBufferedEventsWithSafetyInfo(eventsToProcess, unsafeIds);
        }
        else
        {
            // Even if no events to process, trigger safe promotion
            await TriggerSafePromotion();
        }
    }
    
    /// <summary>
    ///     Process buffered events with knowledge of their safe/unsafe status
    /// </summary>
    private async Task ProcessBufferedEventsWithSafetyInfo(List<Event> events, HashSet<string> unsafeIds)
    {
        if (_projectionActor == null || events.Count == 0) return;
        
        try
        {
            var projectorName = this.GetPrimaryKeyString();
            
            // Separate events based on whether they were marked as unsafe
            var safeEvents = new List<Event>();
            var unsafeEvents = new List<Event>();
            
            foreach (var ev in events)
            {
                if (unsafeIds.Contains(ev.Id.ToString()))
                {
                    unsafeEvents.Add(ev);
                }
                else
                {
                    // Re-evaluate based on current safe window
                    var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);
                    var safeThreshold = GetSafeWindowThreshold();
                    
                    if (eventTime.IsEarlierThanOrEqual(safeThreshold))
                    {
                        safeEvents.Add(ev);
                    }
                    else
                    {
                        unsafeEvents.Add(ev);
                    }
                }
            }
            
            // Process all events together - the actor will determine safe/unsafe based on current time
            // The second parameter is finishedCatchUp, not withinSafeWindow!
            Console.WriteLine($"[{projectorName}] Processing {events.Count} buffered events");
            await _projectionActor.AddEventsAsync(events, true, EventSource.Stream);
            _eventsProcessed += events.Count;
            
            // Update position
            var maxSortableId = events
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
                .Last()
                .SortableUniqueIdValue;
            _state.State.LastPosition = maxSortableId;
            
            Console.WriteLine($"[{projectorName}] ✓ Processed {events.Count} buffered events - Total: {_eventsProcessed:N0} events");
            
            // Trigger safe promotion after processing buffered events
            // This ensures that events transition from unsafe to safe as time passes
            await TriggerSafePromotion();
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to process buffered events: {ex.Message}";
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Error processing buffered events: {ex}");
        }
    }

    // Orleans stream batch observer - processes events in batches for efficiency
    private class StreamBatchObserver : IAsyncBatchObserver<Event>
    {
        private readonly MultiProjectionGrain _grain;

        public StreamBatchObserver(MultiProjectionGrain grain) => _grain = grain;

        // Batch processing method - Orleans v9.0+ uses IList<SequentialItem<T>>
        public Task OnNextAsync(IList<SequentialItem<Event>> batch)
        {
            var events = batch.Select(item => item.Item).ToList();
            Console.WriteLine($"[StreamBatchObserver-{_grain.GetPrimaryKeyString()}] Received batch of {events.Count} events");
            _grain.EnqueueStreamEvents(events);
            return Task.CompletedTask;
        }

        // Legacy batch method for compatibility
        public Task OnNextBatchAsync(IEnumerable<Event> batch, StreamSequenceToken? token = null)
        {
            var events = batch.ToList();
            Console.WriteLine($"[StreamBatchObserver-{_grain.GetPrimaryKeyString()}] Received legacy batch of {events.Count} events");
            _grain.EnqueueStreamEvents(events);
            return Task.CompletedTask;
        }

        public Task OnNextAsync(Event item, StreamSequenceToken? token = null)
        {
            // Single event fallback - enqueue as batch of 1
            Console.WriteLine($"[StreamBatchObserver-{_grain.GetPrimaryKeyString()}] Received single event {item.EventType}, ID: {item.Id}");
            _grain.EnqueueStreamEvents(new[] { item });
            return Task.CompletedTask;
        }

        public Task OnCompletedAsync() 
        {
            Console.WriteLine($"[StreamBatchObserver-{_grain.GetPrimaryKeyString()}] Stream completed");
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            Console.WriteLine($"[StreamBatchObserver-{_grain.GetPrimaryKeyString()}] Stream error: {ex}");
            _grain._lastError = $"Stream error: {ex.Message}";
            return Task.CompletedTask;
        }
    }

    // (removed test-only projector tag scoping)

    internal void EnqueueStreamEvents(IEnumerable<Event> events)
    {
        var list = events as IList<Event> ?? events.ToList();
        if (list.Count == 0) return;
        
        // Evaluate each event's safe/unsafe status at the time of enqueuing
        var safeThreshold = GetSafeWindowThreshold();
        
        lock (_eventBuffer)
        {
            foreach (var ev in list)
            {
                _eventBuffer.Add(ev);
                
                // Mark events that are within the safe window as unsafe
                var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);
                if (!eventTime.IsEarlierThanOrEqual(safeThreshold))
                {
                    _unsafeEventIds.Add(ev.Id.ToString());
                }
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
        Console.WriteLine("[SimplifiedPureGrain] Participate called - registering lifecycle stage");
        var stage = GrainLifecycleStage.Activate + 100;
        lifecycle.Subscribe(GetType().FullName!, stage, InitStreamsAsync, CloseStreamsAsync);
        Console.WriteLine($"[SimplifiedPureGrain] Lifecycle stage registered at {stage}");
    }

    private async Task InitStreamsAsync(CancellationToken ct)
    {
        var projectorName = this.GetPrimaryKeyString();
        Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] InitStreamsAsync called in lifecycle stage");
        
        var streamInfo = _subscriptionResolver.Resolve(projectorName);
        if (streamInfo is not OrleansSekibanStream orleansStream)
        {
            throw new InvalidOperationException($"Invalid stream type: {streamInfo?.GetType().Name}");
        }

        var streamProvider = this.GetStreamProvider(orleansStream.ProviderName);
        _orleansStream = streamProvider.GetStream<Event>(
            StreamId.Create(orleansStream.StreamNamespace, orleansStream.StreamId));
        // Do NOT subscribe here. Subscription will start lazily on first query/state access.
        Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Stream prepared (lazy subscription)");
        _isInitialized = true;
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
