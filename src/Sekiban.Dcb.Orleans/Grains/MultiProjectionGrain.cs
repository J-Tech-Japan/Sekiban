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
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Snapshots;
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
    
    // Orleans infrastructure
    private IAsyncStream<Event>? _orleansStream;
    private StreamSubscriptionHandle<Event>? _orleansStreamHandle;
    private IDisposable? _persistTimer;
    private IDisposable? _fallbackTimer;
    
    // Core projection actor - contains business logic
    private GeneralMultiProjectionActor? _projectionActor;
    
    // Simple tracking
    private bool _isInitialized;
    private string? _lastError;
    private long _eventsProcessed;
    private DateTime? _lastEventTime;

    // Delegate these to configuration
    private readonly int _persistBatchSize = 100;
    private readonly TimeSpan _persistInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _fallbackCheckInterval = TimeSpan.FromSeconds(30);
    private readonly int _maxStateSize = 2 * 1024 * 1024;

    public MultiProjectionGrain(
        [PersistentState("multiProjection", "OrleansStorage")] IPersistentState<MultiProjectionGrainState> state,
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IEventSubscriptionResolver? subscriptionResolver = null,
        IBlobStorageSnapshotAccessor? snapshotAccessor = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _subscriptionResolver = subscriptionResolver ?? new DefaultOrleansEventSubscriptionResolver();
        _snapshotAccessor = snapshotAccessor;
    }

    public async Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();
        
        if (_projectionActor == null)
        {
            return ResultBox.Error<MultiProjectionState>(
                new InvalidOperationException("Projection actor not initialized"));
        }

        return await _projectionActor.GetStateAsync(canGetUnsafeState);
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

        // Delegate to projection actor
        await _projectionActor.AddEventsAsync(events, finishedCatchUp);
        _eventsProcessed += events.Count;
        
        if (events.Count > 0)
        {
            _lastEventTime = DateTime.UtcNow;
        }
    }

    public async Task<MultiProjectionGrainStatus> GetStatusAsync()
    {
        var currentPosition = _state.State?.LastPosition;
        var isCaughtUp = _orleansStreamHandle != null;
        
        long stateSize = 0;
        if (_projectionActor != null)
        {
            var snapshot = await _projectionActor.GetSnapshotAsync();
            if (snapshot.IsSuccess)
            {
                var json = JsonSerializer.Serialize(snapshot.GetValue(), _domainTypes.JsonSerializerOptions);
                stateSize = Encoding.UTF8.GetByteCount(json);
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
            !string.IsNullOrEmpty(_lastError),
            _lastError);
    }

    public async Task<ResultBox<bool>> PersistStateAsync()
    {
        try
        {
            if (_projectionActor == null)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("Projection actor not initialized"));
            }

            // Ask actor to build a persistable snapshot (with size validation + offload included)
            var persistable = await _projectionActor.BuildSnapshotForPersistenceAsync(false);
            if (!persistable.IsSuccess)
            {
                _lastError = persistable.GetException().Message;
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] WARNING: {_lastError}");
                return ResultBox.FromValue(false);
            }

            var data = persistable.GetValue();
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Persisting state: Size={data.Size}, Events={_eventsProcessed}, SafePosition={data.SafeLastSortableUniqueId}");

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
            
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _lastError = $"Persistence failed: {ex.Message}";
            return ResultBox.Error<bool>(ex);
        }
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
        
        // Subscribe to Orleans stream if not already subscribed
        if (_orleansStreamHandle == null && _orleansStream != null)
        {
            try
            {
                var projectorName = this.GetPrimaryKeyString();
                Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Starting subscription to Orleans stream");
                
                var observer = new StreamObserver(this);
                _orleansStreamHandle = await _orleansStream.SubscribeAsync(observer, null);
                
                Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Successfully subscribed to Orleans stream!");
            }
            catch (Exception ex)
            {
                var projectorName = this.GetPrimaryKeyString();
                Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] ERROR: Failed to subscribe to Orleans stream: {ex}");
                _lastError = $"Stream subscription failed: {ex.Message}";
                throw;
            }
        }
        else if (_orleansStreamHandle != null)
        {
            var projectorName = this.GetPrimaryKeyString();
            Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Stream subscription already active");
        }
        
        await CatchUpFromEventStoreAsync();
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
            var stateResult = await _projectionActor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                return new QueryResultGeneral(null!, string.Empty, query);
            }

            var state = stateResult.GetValue();
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload!));
            var result = await _domainTypes.QueryTypes.ExecuteQueryAsync(
                query, 
                projectorProvider, 
                ServiceProvider);

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

        if (_eventsProcessed == 0)
        {
            await RefreshAsync();
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
            var result = await _domainTypes.QueryTypes.ExecuteListQueryAsGeneralAsync(
                query,
                projectorProvider,
                ServiceProvider);

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
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Refreshing from event store");
        await CatchUpFromEventStoreAsync();
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var projectorName = this.GetPrimaryKeyString();
        Console.WriteLine($"[SimplifiedPureGrain] OnActivateAsync for {projectorName}");

        // Create projection actor
        if (_projectionActor == null)
        {
            Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Creating new projection actor");
            _projectionActor = new GeneralMultiProjectionActor(
                _domainTypes,
                projectorName,
                new GeneralMultiProjectionActorOptions
                {
                    SafeWindowMs = 20000, // 20 seconds
                    SnapshotAccessor = _snapshotAccessor,
                    SnapshotOffloadThresholdBytes = 2 * 1024 * 1024,
                    MaxSnapshotSerializedSizeBytes = 2 * 1024 * 1024
                });

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
                        Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] State restored - Events processed: {_eventsProcessed}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Failed to restore state: {ex.Message}. Falling back to event store catch-up and clearing snapshot.");
                    // Clear invalid snapshot and persist cleared state to avoid repeated failures
                    try
                    {
                        _state.State.SerializedState = null;
                        _state.State.EventsProcessed = 0;
                        await _state.WriteStateAsync();
                    }
                    catch (Exception clearEx)
                    {
                        Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Failed to clear invalid snapshot: {clearEx.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] No persisted state to restore");
            }
        }

        await base.OnActivateAsync(cancellationToken);

        // After activation, always catch up from the event store to ensure up-to-date state
        await CatchUpFromEventStoreAsync();
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

        _persistTimer?.Dispose();
        _fallbackTimer?.Dispose();

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

    private async Task CatchUpFromEventStoreAsync()
    {
        if (_projectionActor == null || _eventStore == null) return;

        // Get current position
        SortableUniqueId? fromPosition = null;
        var currentState = await _projectionActor.GetStateAsync();
        if (currentState.IsSuccess)
        {
            var state = currentState.GetValue();
            if (!string.IsNullOrEmpty(state.LastSortableUniqueId))
            {
                fromPosition = new SortableUniqueId(state.LastSortableUniqueId);
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Current position: {fromPosition.Value}");
            }
        }

        // Load events from store
        var eventsResult = fromPosition == null
            ? await _eventStore.ReadAllEventsAsync(since: null)
            : await _eventStore.ReadAllEventsAsync(since: fromPosition.Value);

        if (eventsResult.IsSuccess)
        {
            var events = eventsResult.GetValue().ToList();
            if (events.Any())
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Processing {events.Count} events from store");
                await AddEventsAsync(events, true);
                await PersistStateAsync();
            }
        }
    }

    /// <summary>
    ///     Process stream event
    /// </summary>
    internal async Task ProcessStreamEvent(Event evt)
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] ProcessStreamEvent: {evt.EventType}, SortableUniqueId: {evt.SortableUniqueIdValue}");
        
        if (!_isInitialized || _projectionActor == null)
        {
            await EnsureInitializedAsync();
        }

        if (_projectionActor == null) return;

        try
        {
            // Check for duplicate
            if (await _projectionActor.IsSortableUniqueIdReceived(evt.SortableUniqueIdValue))
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Event {evt.Id} already processed, skipping");
                return;
            }

            // Process the event
            await _projectionActor.AddEventsAsync(new[] { evt }, false);
            _eventsProcessed++;
            _lastEventTime = DateTime.UtcNow;

            // Update position
            _state.State.LastPosition = evt.SortableUniqueIdValue;

            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Event processed successfully, total: {_eventsProcessed}");
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to process stream event: {ex.Message}";
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Error processing stream event: {ex}");
        }
    }

    // Orleans stream observer
    private class StreamObserver : IAsyncObserver<Event>
    {
        private readonly MultiProjectionGrain _grain;

        public StreamObserver(MultiProjectionGrain grain) => _grain = grain;

        public async Task OnNextAsync(Event item, StreamSequenceToken? token = null)
        {
            Console.WriteLine($"[StreamObserver-{_grain.GetPrimaryKeyString()}] Received event {item.EventType}, ID: {item.Id}");
            await _grain.ProcessStreamEvent(item);
        }

        public Task OnCompletedAsync() 
        {
            Console.WriteLine($"[StreamObserver-{_grain.GetPrimaryKeyString()}] Stream completed");
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            Console.WriteLine($"[StreamObserver-{_grain.GetPrimaryKeyString()}] Stream error: {ex}");
            _grain._lastError = $"Stream error: {ex.Message}";
            return Task.CompletedTask;
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

        try
        {
            Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Getting stream for {orleansStream.StreamNamespace}/{orleansStream.StreamId}");
            
            var observer = new StreamObserver(this);
            _orleansStreamHandle = await _orleansStream.SubscribeAsync(observer, null);
            
            Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] Successfully subscribed to Orleans stream!");
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimplifiedPureGrain-{projectorName}] ERROR: Failed to subscribe to Orleans stream: {ex}");
            _lastError = $"Stream subscription failed: {ex.Message}";
        }
    }

    private Task CloseStreamsAsync(CancellationToken ct) =>
        _orleansStreamHandle?.UnsubscribeAsync() ?? Task.CompletedTask;
    #endregion
}
