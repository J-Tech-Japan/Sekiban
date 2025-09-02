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
        if (_projectionActor == null || _eventStore == null) return;

        // Coalesce frequent calls: skip if another catch-up is running
        if (_catchUpRunning)
        {
            return;
        }
        // Rate-limit non-forced catch-ups to avoid thrashing and duplicates
        if (!forceFull && (DateTime.UtcNow - _lastCatchUpUtc) < _minCatchUpInterval)
        {
            return;
        }
        // If stream is actively delivering events recently, skip catch-up to avoid overlap
        if (!forceFull && _lastEventTime != null && (DateTime.UtcNow - _lastEventTime.Value) < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _catchUpRunning = true;
        
        try
        {
            // Get current position
            SortableUniqueId? fromPosition = null;
            if (!forceFull)
            {
                // Use SAFE state for determining catch-up position to avoid
                // skipping events that are only present in the unsafe window
                var currentState = await _projectionActor.GetStateAsync(canGetUnsafeState: false);
                if (currentState.IsSuccess)
                {
                    var state = currentState.GetValue();
                    if (!string.IsNullOrEmpty(state.LastSortableUniqueId))
                    {
                        fromPosition = new SortableUniqueId(state.LastSortableUniqueId);
                        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Resuming from checkpoint: {fromPosition.Value}");
                    }
                }

                // Fallback to persisted positions if actor has none
                if (fromPosition == null && !string.IsNullOrEmpty(_state.State?.SafeLastPosition))
                {
                    fromPosition = new SortableUniqueId(_state.State.SafeLastPosition);
                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] Using persisted SafeLastPosition: {fromPosition.Value}");
                }
                else if (fromPosition == null && !string.IsNullOrEmpty(_state.State?.LastPosition))
                {
                    fromPosition = new SortableUniqueId(_state.State.LastPosition);
                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] Using persisted LastPosition: {fromPosition.Value}");
                }
            }

            // Load events from store with conditional overlap to avoid boundary misses without thrashing
            SortableUniqueId? overlappedFrom = null;
            if (fromPosition != null)
            {
                // Only apply overlap if last catch-up was not very recent
                var shouldOverlap = (DateTime.UtcNow - _lastCatchUpUtc) > _overlapCooldown;
                if (_avoidOverlapOnce)
                {
                    overlappedFrom = fromPosition;
                    _avoidOverlapOnce = false;
                }
                else if (shouldOverlap)
                {
                    var overlapValue = fromPosition.GetSafeId();
                    overlappedFrom = new SortableUniqueId(overlapValue);
                }
                else
                {
                    overlappedFrom = fromPosition;
                }
            }

            // Read all events from store (projector-specific filtering is handled by projector logic)
            var eventsResult = overlappedFrom == null
                ? await _eventStore.ReadAllEventsAsync(since: null)
                : await _eventStore.ReadAllEventsAsync(since: overlappedFrom.Value);

            if (eventsResult.IsSuccess)
            {
                var events = eventsResult.GetValue().ToList();
                if (events.Any())
                {
                    string? currentLastSortable = null;
                    try
                    {
                        var unsafeState = await _projectionActor.GetStateAsync(true);
                        if (unsafeState.IsSuccess)
                        {
                            var val = unsafeState.GetValue();
                            if (!string.IsNullOrEmpty(val.LastSortableUniqueId)) currentLastSortable = val.LastSortableUniqueId;
                        }
                    }
                    catch { }

                    int skippedAlreadyApplied = 0;
                    var filtered = new List<Sekiban.Dcb.Events.Event>(events.Count);
                    foreach (var ev in events)
                    {
                        if (currentLastSortable != null && string.Compare(ev.SortableUniqueIdValue, currentLastSortable, StringComparison.Ordinal) <= 0)
                        {
                            skippedAlreadyApplied++;
                            continue;
                        }
                        if (_processedEventIds.Contains(ev.Id.ToString()))
                        {
                            skippedAlreadyApplied++;
                            continue;
                        }
                        filtered.Add(ev);
                    }

                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] Catch-up: {events.Count} events fetched, {filtered.Count} new, {skippedAlreadyApplied} already applied (from: {(overlappedFrom?.Value ?? "start")} to: {(currentLastSortable ?? "none")})");
                    if (filtered.Count > 0)
                    {
                        await AddEventsAsync(filtered, true);
                        await PersistStateAsync();
                    }
                }
            }
        }
        finally
        {
            _lastCatchUpUtc = DateTime.UtcNow;
            _catchUpRunning = false;
        }
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

        // Filter out already processed events to prevent double counting
        var newEvents = events.Where(e => !_processedEventIds.Contains(e.Id.ToString())).ToList();
        
        if (newEvents.Count > 0)
        {
            // Forward only new events to the actor
            await _projectionActor.AddEventsAsync(newEvents, true, Sekiban.Dcb.Actors.EventSource.Stream);
            _eventsProcessed += newEvents.Count;
            
            // Mark events as processed
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

            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ✓ Processed {events.Count} events - Total: {_eventsProcessed:N0} events");

            // Persist state after processing a batch if it's large enough
            if (events.Count >= _persistBatchSize)
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
        lock (_eventBuffer)
        {
            if (_eventBuffer.Count == 0) return;
            
            eventsToProcess = new List<Event>(_eventBuffer);
            _eventBuffer.Clear();
            _lastBufferFlush = DateTime.UtcNow;
        }

        await ProcessEventBatch(eventsToProcess);
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
        lock (_eventBuffer)
        {
            _eventBuffer.AddRange(list);
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
