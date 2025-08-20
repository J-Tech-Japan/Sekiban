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

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Just prepare basic setup during activation
        // Actual initialization happens on first access
        var projectorName = this.GetPrimaryKeyString();
        
        // Create the projection actor
        _projectionActor = new GeneralMultiProjectionActor(
            _domainTypes,
            projectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = 20000 // 20 seconds
            });
        
        // Create event provider but don't start it yet
        _eventProvider = new GeneralEventProvider(
            _eventStore,
            _eventSubscription,
            TimeSpan.FromSeconds(20));
        
        return base.OnActivateAsync(cancellationToken);
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
        if (_isInitialized)
            return;
        
        _isInitialized = true;
        
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
        if (_isSubscriptionActive || _eventProvider == null || _projectionActor == null)
            return;
        
        try
        {
            // Determine starting position
            SortableUniqueId? fromPosition = null;
            if (!string.IsNullOrEmpty(_state.State?.LastPosition))
            {
                fromPosition = new SortableUniqueId(_state.State.LastPosition);
            }
            
            // Start the event provider
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
                return new QueryResultGeneral(
                    null!, 
                    string.Empty, 
                    query);
            }
            
            var state = stateResult.GetValue();
            
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
            return ListQueryResultGeneral.Empty;
        }
        
        try
        {
            // Get the current state
            var stateResult = await _projectionActor.GetStateAsync(true);
            if (!stateResult.IsSuccess)
            {
                return ListQueryResultGeneral.Empty;
            }
            
            var state = stateResult.GetValue();
            
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