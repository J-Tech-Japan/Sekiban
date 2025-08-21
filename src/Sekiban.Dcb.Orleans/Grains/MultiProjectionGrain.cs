using Orleans;
using Orleans.Runtime;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using System.Text;
using System.Text.Json;

namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
/// Orleans grain implementation for multi-projection
/// </summary>
public class MultiProjectionGrain : Grain, IMultiProjectionGrain
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly IEventSubscription _eventSubscription;
    private readonly IPersistentState<MultiProjectionGrainState> _state;
    
    private GeneralMultiProjectionActor? _projectionActor;
    private GeneralEventProvider? _eventProvider;
    private IEventSubscriptionHandle? _subscriptionHandle;
    private IEventProviderHandle? _providerHandle;
    
    private bool _isInitialized;
    private bool _isSubscriptionActive;
    private DateTime? _lastEventTime;
    private DateTime? _lastPersistTime;
    private long _eventsProcessed;
    private string? _lastError;
    private readonly TimeSpan _persistInterval = TimeSpan.FromMinutes(5);
    private readonly int _maxStateSize = 2 * 1024 * 1024; // 2MB default limit
    
    private IDisposable? _persistTimer;

    public MultiProjectionGrain(
        [PersistentState("multiProjection", "OrleansStorage")] IPersistentState<MultiProjectionGrainState> state,
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IEventSubscription eventSubscription)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _eventSubscription = eventSubscription ?? throw new ArgumentNullException(nameof(eventSubscription));
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Just prepare basic setup during activation
        // Actual initialization happens on first access
        var projectorName = this.GetPrimaryKeyString();
        
        Console.WriteLine($"[MultiProjectionGrain] OnActivateAsync for {projectorName}");
        
        // Create the projection actor
        _projectionActor = new GeneralMultiProjectionActor(
            _domainTypes,
            projectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = 20000 // 20 seconds
            });
        
        // For debugging: Check if event store is available
        if (_eventStore == null)
        {
            Console.WriteLine($"[MultiProjectionGrain] WARNING: _eventStore is null for {projectorName}");
        }
        else
        {
            Console.WriteLine($"[MultiProjectionGrain] _eventStore is available for {projectorName}");
            
            // Try to read events to verify connection
            try
            {
                var testResult = await _eventStore.ReadAllEventsAsync();
                if (testResult.IsSuccess)
                {
                    var count = testResult.GetValue().Count();
                    Console.WriteLine($"[MultiProjectionGrain] Event store test successful, found {count} events");
                }
                else
                {
                    Console.WriteLine($"[MultiProjectionGrain] Event store test failed: {testResult.GetException()?.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MultiProjectionGrain] Event store test exception: {ex.Message}");
            }
        }
        
        // Create event provider but don't start it yet
        _eventProvider = new GeneralEventProvider(
            _eventStore,
            _eventSubscription,
            TimeSpan.FromSeconds(20));
        
        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        // Clean up resources
        await StopSubscriptionInternalAsync();
        
        _persistTimer?.Dispose();
        _eventProvider?.Dispose();
        
        await base.OnDeactivateAsync(reason, cancellationToken);
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

    public async Task<ResultBox<SerializableMultiProjectionState>> GetSerializableStateAsync(bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();
        
        if (_projectionActor == null)
        {
            return ResultBox.Error<SerializableMultiProjectionState>(
                new InvalidOperationException("Projection actor not initialized"));
        }
        
        return await _projectionActor.GetSerializableStateAsync(canGetUnsafeState);
    }

    public async Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true)
    {
        await EnsureInitializedAsync();
        
        if (_projectionActor == null)
        {
            throw new InvalidOperationException("Projection actor not initialized");
        }
        
        await _projectionActor.AddEventsAsync(events, finishedCatchUp);
        _eventsProcessed += events.Count;
        
        if (events.Count > 0)
        {
            _lastEventTime = DateTime.UtcNow;
        }
    }

    public async Task<MultiProjectionGrainStatus> GetStatusAsync()
    {
        string? currentPosition = null;
        bool isCaughtUp = false;
        
        if (_eventProvider != null)
        {
            currentPosition = _eventProvider.CurrentPosition?.Value;
            isCaughtUp = _eventProvider.IsCaughtUp;
        }
        
        // Calculate state size
        long stateSize = 0;
        if (_projectionActor != null)
        {
            var serializableState = await _projectionActor.GetSerializableStateAsync(true);
            if (serializableState.IsSuccess)
            {
                var json = JsonSerializer.Serialize(serializableState.GetValue(), _domainTypes.JsonSerializerOptions);
                stateSize = Encoding.UTF8.GetByteCount(json);
            }
        }
        
        return new MultiProjectionGrainStatus(
            ProjectorName: this.GetPrimaryKeyString(),
            IsSubscriptionActive: _isSubscriptionActive,
            IsCaughtUp: isCaughtUp,
            CurrentPosition: currentPosition,
            EventsProcessed: _eventsProcessed,
            LastEventTime: _lastEventTime,
            LastPersistTime: _lastPersistTime,
            StateSize: stateSize,
            HasError: _lastError != null,
            LastError: _lastError);
    }

    public async Task<ResultBox<bool>> PersistStateAsync()
    {
        try
        {
            if (_projectionActor == null)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("Projection actor not initialized"));
            }
            
            // Get the serializable state
            var stateResult = await _projectionActor.GetSerializableStateAsync(false); // Only persist safe state
            if (!stateResult.IsSuccess)
            {
                return ResultBox.Error<bool>(stateResult.GetException());
            }
            
            var serializableState = stateResult.GetValue();
            
            // Serialize to check size
            var json = JsonSerializer.Serialize(serializableState, _domainTypes.JsonSerializerOptions);
            var stateSize = Encoding.UTF8.GetByteCount(json);
            
            // Check size limit
            if (stateSize > _maxStateSize)
            {
                // Log warning but don't fail - just skip this persist
                _lastError = $"State size {stateSize} exceeds limit {_maxStateSize}, skipping persist";
                return ResultBox.FromValue(false); // Return false to indicate skipped
            }
            
            // Update grain state
            _state.State.ProjectorName = this.GetPrimaryKeyString();
            _state.State.SerializedState = json;
            _state.State.LastPosition = _eventProvider?.CurrentPosition?.Value;
            _state.State.LastPersistTime = DateTime.UtcNow;
            _state.State.EventsProcessed = _eventsProcessed;
            _state.State.StateSize = stateSize;
            
            // Persist to storage
            await _state.WriteStateAsync();
            
            _lastPersistTime = DateTime.UtcNow;
            _lastError = null; // Clear error on successful persist
            
            return ResultBox.FromValue(true); // Return true for success
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to persist state: {ex.Message}";
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task StopSubscriptionAsync()
    {
        await StopSubscriptionInternalAsync();
    }

    public async Task StartSubscriptionAsync()
    {
        await EnsureInitializedAsync();
    }

    private async Task EnsureInitializedAsync()
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] EnsureInitializedAsync called, _isInitialized={_isInitialized}");
        
        if (_isInitialized)
            return;
        
        _isInitialized = true;
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Initializing grain...");
        
        // Restore state if available
        if (_state.State != null && !string.IsNullOrEmpty(_state.State.SerializedState))
        {
            try
            {
                var deserializedState = JsonSerializer.Deserialize<SerializableMultiProjectionState>(
                    _state.State.SerializedState,
                    _domainTypes.JsonSerializerOptions);
                
                if (deserializedState != null && _projectionActor != null)
                {
                    await _projectionActor.SetCurrentState(deserializedState);
                    _eventsProcessed = _state.State.EventsProcessed;
                }
            }
            catch (Exception ex)
            {
                _lastError = $"Failed to restore state: {ex.Message}";
                // Continue with fresh state
            }
        }
        
        // Start event subscription
        await StartSubscriptionInternalAsync();
        
        // Set up periodic persistence timer
        _persistTimer = this.RegisterGrainTimer(
            async () => await PersistStateAsync(),
            new() { 
                DueTime = _persistInterval, 
                Period = _persistInterval, 
                Interleave = true 
            });
    }

    private async Task StartSubscriptionInternalAsync()
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] StartSubscriptionInternalAsync called");
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] _isSubscriptionActive={_isSubscriptionActive}, _eventProvider={(_eventProvider != null ? "set" : "null")}, _projectionActor={(_projectionActor != null ? "set" : "null")}");
        
        if (_isSubscriptionActive || _eventProvider == null || _projectionActor == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Skipping subscription start - already active or missing components");
            return;
        }
        
        try
        {
            // Determine starting position
            SortableUniqueId? fromPosition = null;
            if (!string.IsNullOrEmpty(_state.State?.LastPosition))
            {
                fromPosition = new SortableUniqueId(_state.State.LastPosition);
            }
            
            // First, catch up with historical events from the event store
            if (fromPosition == null)
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Starting initial catchup from beginning");
                
                // If no position is saved, load all events from the beginning
                var historicalEventsResult = await _eventStore.ReadAllEventsAsync(since: null);
                
                if (historicalEventsResult.IsSuccess)
                {
                    var historicalEvents = historicalEventsResult.GetValue().ToList();
                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] Loaded {historicalEvents.Count} historical events");
                    
                    // Log details of each event for debugging
                    foreach (var evt in historicalEvents)
                    {
                        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Event: Type={evt.EventType}, ID={evt.Id}, Tags=[{string.Join(", ", evt.Tags)}]");
                    }
                    
                    if (historicalEvents.Any())
                    {
                        await _projectionActor.AddEventsAsync(historicalEvents, finishedCatchUp: true);
                        _eventsProcessed += historicalEvents.Count;
                        _lastEventTime = DateTime.UtcNow;
                        
                        // Update position to the last processed event
                        var lastEvent = historicalEvents.Last();
                        fromPosition = new SortableUniqueId(lastEvent.SortableUniqueIdValue);
                        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Catchup complete. Last position: {fromPosition.Value}");
                    }
                }
                else
                {
                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] Failed to load historical events: {historicalEventsResult.GetException()?.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Resuming from position: {fromPosition.Value}");
            }
            
            // Start the event provider for ongoing events
            _providerHandle = await _eventProvider.StartAsync(
                async (evt, isCaughtUp) =>
                {
                    await _projectionActor.AddEventsAsync(new[] { evt }, isCaughtUp);
                    _eventsProcessed++;
                    _lastEventTime = DateTime.UtcNow;
                },
                fromPosition,
                eventTopic: "event.all",
                filter: null,
                batchSize: 1000);
            
            _isSubscriptionActive = true;
            _lastError = null;
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to start subscription: {ex.Message}";
            _isSubscriptionActive = false;
            throw;
        }
    }

    private async Task StopSubscriptionInternalAsync()
    {
        if (_subscriptionHandle != null)
        {
            await _subscriptionHandle.UnsubscribeAsync();
            _subscriptionHandle = null;
        }
        
        if (_providerHandle != null)
        {
            _providerHandle.Dispose();
            _providerHandle = null;
        }
        
        _isSubscriptionActive = false;
    }
    
    public async Task<QueryResultGeneral> ExecuteQueryAsync(IQueryCommon query)
    {
        await EnsureInitializedAsync();
        
        if (_projectionActor == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: Projection actor is null");
            return new QueryResultGeneral(
                null!, 
                string.Empty, 
                query);
        }
        
        try
        {
            // Get the current state
            var stateResult = await _projectionActor.GetStateAsync(true);
            if (!stateResult.IsSuccess)
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: Failed to get state - {stateResult.GetException()?.Message}");
                return new QueryResultGeneral(
                    null!, 
                    string.Empty, 
                    query);
            }
            
            var state = stateResult.GetValue();
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: Got state - Version={state.Version}, IsCaughtUp={state.IsCatchedUp}, LastEventId={state.LastEventId}");
            
            // Create a provider function that returns the payload
            Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider = () =>
                Task.FromResult(ResultBox.FromValue(state.Payload));
            
            // Get service provider from grain context
            var serviceProvider = ServiceProvider;
            
            // Execute the query using QueryTypes
            var result = await _domainTypes.QueryTypes.ExecuteQueryAsync(
                query, 
                projectorProvider, 
                serviceProvider);
                
            if (result.IsSuccess)
            {
                var value = result.GetValue();
                return new QueryResultGeneral(
                    value, 
                    value?.GetType().FullName ?? string.Empty, 
                    query);
            }
            
            return new QueryResultGeneral(
                null!, 
                string.Empty, 
                query);
        }
        catch (Exception ex)
        {
            _lastError = $"Query execution failed: {ex.Message}";
            return new QueryResultGeneral(
                null!, 
                string.Empty, 
                query);
        }
    }
    
    public async Task<ListQueryResultGeneral> ExecuteListQueryAsync(IListQueryCommon query)
    {
        await EnsureInitializedAsync();
        
        if (_projectionActor == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ProjectionActor is null");
            return ListQueryResultGeneral.Empty;
        }
        
        try
        {
            // Get the current state
            var stateResult = await _projectionActor.GetStateAsync(true);
            if (!stateResult.IsSuccess)
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Failed to get state: {stateResult.GetException()?.Message}");
                return ListQueryResultGeneral.Empty;
            }
            
            var state = stateResult.GetValue();
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] State retrieved. Payload type: {state.Payload?.GetType().Name}, Events processed: {_eventsProcessed}");
            
            // Create a provider function that returns the payload
            Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider = () =>
                Task.FromResult(ResultBox.FromValue(state.Payload));
            
            // Get service provider from grain context
            var serviceProvider = ServiceProvider;
            
            // Execute the list query using QueryTypes
            var result = await _domainTypes.QueryTypes.ExecuteListQueryAsync(
                query, 
                projectorProvider, 
                serviceProvider);
                
            if (result.IsSuccess)
            {
                var value = result.GetValue();
                if (value is ListQueryResult<object> listResult)
                {
                    return new ListQueryResultGeneral(
                        listResult.TotalCount,
                        listResult.TotalPages,
                        listResult.CurrentPage,
                        listResult.PageSize,
                        listResult.Items,
                        listResult.Items.FirstOrDefault()?.GetType().FullName ?? string.Empty,
                        query);
                }
            }
            
            return ListQueryResultGeneral.Empty;
        }
        catch (Exception ex)
        {
            _lastError = $"List query execution failed: {ex.Message}";
            return ListQueryResultGeneral.Empty;
        }
    }
}

/// <summary>
/// Persistent state for the multi-projection grain
/// </summary>
[GenerateSerializer]
public class MultiProjectionGrainState
{
    [Id(0)] public string ProjectorName { get; set; } = string.Empty;
    [Id(1)] public string? SerializedState { get; set; }
    [Id(2)] public string? LastPosition { get; set; }
    [Id(3)] public DateTime LastPersistTime { get; set; }
    [Id(4)] public long EventsProcessed { get; set; }
    [Id(5)] public long StateSize { get; set; }
}