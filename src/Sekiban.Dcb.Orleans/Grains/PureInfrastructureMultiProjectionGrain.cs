using Orleans.Streams;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.MultiProjections;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Pure infrastructure grain with NO business logic
///     All business decisions are delegated to the orchestrator
/// </summary>
public class PureInfrastructureMultiProjectionGrain : Grain, IMultiProjectionGrain, ILifecycleParticipant<IGrainLifecycle>
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly IPersistentState<MultiProjectionGrainState> _state;
    private readonly IEventSubscriptionResolver _subscriptionResolver;
    
    // Orleans infrastructure
    private IAsyncStream<Event>? _orleansStream;
    private StreamSubscriptionHandle<Event>? _orleansStreamHandle;
    private IDisposable? _persistTimer;
    private IDisposable? _fallbackTimer;
    
    // Orchestrator (contains ALL business logic)
    private IProjectionOrchestratorV2? _orchestrator;
    
    // Simple infrastructure state (no business logic)
    private bool _isInitialized;
    private string? _lastError;

    public PureInfrastructureMultiProjectionGrain(
        [PersistentState("multiProjection", "OrleansStorage")] IPersistentState<MultiProjectionGrainState> state,
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IEventSubscriptionResolver? subscriptionResolver = null,
        IProjectionOrchestratorV2? orchestrator = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _subscriptionResolver = subscriptionResolver ?? new DefaultOrleansEventSubscriptionResolver();
        _orchestrator = orchestrator;
    }

    public async Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();
        
        var state = _orchestrator?.GetCurrentState();
        if (state == null)
        {
            return ResultBox.Error<MultiProjectionState>(
                new InvalidOperationException("No state available"));
        }

        // Convert from orchestrator's ProjectionState to MultiProjectionState
        return ResultBox.FromValue(new MultiProjectionState(
            state.Payload,
            state.LastPosition ?? string.Empty,
            state.SafePosition ?? string.Empty,
            string.Empty, // LastEventId not tracked in new model
            state.Version,
            state.IsCaughtUp,
            state.EventsProcessed));
    }

    public async Task<ResultBox<SerializableMultiProjectionStateDto>> GetSerializableStateAsync(bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();

        if (_orchestrator == null)
        {
            return ResultBox.Error<SerializableMultiProjectionStateDto>(
                new InvalidOperationException("Orchestrator not initialized"));
        }

        var result = await _orchestrator.GetSerializableStateAsync(canGetUnsafeState);
        if (!result.IsSuccess)
        {
            return ResultBox.Error<SerializableMultiProjectionStateDto>(result.GetException());
        }

        var serialized = result.GetValue();
        
        // Deserialize to get the actual state for DTO conversion
        var deserializedState = System.Text.Json.JsonSerializer.Deserialize<SerializableMultiProjectionState>(
            serialized.PayloadJson,
            _domainTypes.JsonSerializerOptions);
        
        if (deserializedState == null)
        {
            return ResultBox.Error<SerializableMultiProjectionStateDto>(
                new InvalidOperationException("Failed to deserialize state"));
        }

        var dto = SerializableMultiProjectionStateDto.FromCore(deserializedState);
        return ResultBox.FromValue(dto);
    }

    public async Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true)
    {
        await EnsureInitializedAsync();

        if (_orchestrator == null)
        {
            throw new InvalidOperationException("Orchestrator not initialized");
        }

        // Delegate ALL business logic to orchestrator
        var result = await _orchestrator.ProcessEventsAsync(events);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to process events: {result.GetException()?.Message}");
        }

        var processResult = result.GetValue();
        
        // Only handle persistence if orchestrator says so
        if (processResult.RequiresPersistence)
        {
            await PersistStateAsync();
        }
    }

    public async Task<MultiProjectionGrainStatus> GetStatusAsync()
    {
        var state = _orchestrator?.GetCurrentState();
        var position = state?.LastPosition;
        var eventsProcessed = state?.EventsProcessed ?? 0;
        
        // Get state size from orchestrator
        var sizeCheck = _orchestrator != null 
            ? await _orchestrator.CheckStateSizeAsync()
            : new StateSizeCheck(0, 0, false, null);

        return new MultiProjectionGrainStatus(
            this.GetPrimaryKeyString(),
            _orleansStreamHandle != null,  // isActive
            state?.IsCaughtUp ?? false,    // isCaughtUp
            position,
            eventsProcessed,
            null,  // lastEventTime not tracked here
            null,  // lastPersistTime not tracked here
            sizeCheck.CurrentSize,
            !string.IsNullOrEmpty(_lastError),
            _lastError);
    }

    public async Task<ResultBox<bool>> PersistStateAsync()
    {
        try
        {
            if (_orchestrator == null)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("Orchestrator not initialized"));
            }

            // Get serializable state from orchestrator
            var stateResult = await _orchestrator.GetSerializableStateAsync(false); // Safe state only
            if (!stateResult.IsSuccess)
            {
                return ResultBox.Error<bool>(stateResult.GetException());
            }

            var serialized = stateResult.GetValue();
            
            // Check size limit (business logic in orchestrator)
            var sizeCheck = await _orchestrator.CheckStateSizeAsync();
            if (sizeCheck.ExceedsLimit)
            {
                _lastError = sizeCheck.Warning;
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] {sizeCheck.Warning}");
                return ResultBox.FromValue(false);
            }

            // Pure infrastructure: just persist what orchestrator provides
            _state.State.ProjectorName = this.GetPrimaryKeyString();
            _state.State.SerializedState = serialized.PayloadJson;
            _state.State.LastPosition = serialized.LastPosition;
            _state.State.SafeLastPosition = serialized.SafePosition;
            _state.State.EventsProcessed = serialized.EventsProcessed;
            _state.State.Version = serialized.Version;
            _state.State.StateSize = serialized.StateSize;
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
        await CatchUpFromEventStoreAsync();
    }

    public async Task<QueryResultGeneral> ExecuteQueryAsync(IQueryCommon query)
    {
        await EnsureInitializedAsync();

        var state = _orchestrator?.GetCurrentState();
        if (state?.Payload == null)
        {
            return new QueryResultGeneral(null!, string.Empty, query);
        }

        try
        {
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload));
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

        var state = _orchestrator?.GetCurrentState();
        if (state?.Payload == null)
        {
            // Try to refresh if no state
            await RefreshAsync();
            state = _orchestrator?.GetCurrentState();
            
            if (state?.Payload == null)
            {
                return ListQueryResultGeneral.Empty;
            }
        }

        try
        {
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload));
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
        
        if (_orchestrator == null) return false;
        
        // Create a dummy event to check
        var dummyEvent = new Event(
            null!,
            sortableUniqueId,
            string.Empty,
            Guid.NewGuid(),
            null!,
            new List<string>());
        
        // Use orchestrator's duplicate check logic
        return !await _orchestrator.ShouldProcessEventAsync(dummyEvent);
    }

    public async Task RefreshAsync()
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Refreshing from event store");
        await CatchUpFromEventStoreAsync();
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var projectorName = this.GetPrimaryKeyString();
        Console.WriteLine($"[PureInfrastructureGrain] OnActivateAsync for {projectorName}");

        // Create orchestrator if not injected
        if (_orchestrator == null)
        {
            _orchestrator = new EnhancedProjectionOrchestrator(_domainTypes);
            _orchestrator.Configure(new OrchestratorConfiguration
            {
                PersistBatchSize = 100,
                PersistInterval = TimeSpan.FromMinutes(5),
                MaxStateSize = 2 * 1024 * 1024,
                SafeWindow = TimeSpan.FromSeconds(20),
                FallbackCheckInterval = TimeSpan.FromSeconds(30),
                EnableDuplicateCheck = true
            });
        }

        // Initialize orchestrator with persisted state
        SerializedProjectionState? persistedState = null;
        if (!string.IsNullOrEmpty(_state.State?.SerializedState))
        {
            persistedState = new SerializedProjectionState
            {
                PayloadJson = _state.State.SerializedState,
                LastPosition = _state.State.LastPosition,
                SafePosition = _state.State.SafeLastPosition,
                EventsProcessed = _state.State.EventsProcessed,
                Version = _state.State.Version ?? 0
            };
        }

        var initResult = await _orchestrator.InitializeAsync(projectorName, persistedState);
        if (!initResult.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to initialize orchestrator: {initResult.GetException()?.Message}");
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[PureInfrastructureGrain-{this.GetPrimaryKeyString()}] Deactivating");
        
        // Check if we should persist before shutdown
        if (_orchestrator != null)
        {
            var persistDecision = await _orchestrator.ShouldPersistAsync();
            if (persistDecision.ShouldPersist)
            {
                await PersistStateAsync();
            }
        }

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
            async () => await CheckAndPersistAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMinutes(5),
                Period = TimeSpan.FromMinutes(5),
                Interleave = true
            });

        // Set up fallback check timer
        _fallbackTimer = this.RegisterGrainTimer(
            async () => await RefreshAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(30),
                Period = TimeSpan.FromMinutes(1),
                Interleave = true
            });
    }

    private async Task CheckAndPersistAsync()
    {
        if (_orchestrator == null) return;
        
        // Let orchestrator decide if persistence is needed
        var decision = await _orchestrator.ShouldPersistAsync();
        if (decision.ShouldPersist)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Periodic persistence triggered: {decision.Reason}");
            await PersistStateAsync();
        }
    }

    private async Task CatchUpFromEventStoreAsync()
    {
        if (_orchestrator == null || _eventStore == null) return;

        var state = _orchestrator.GetCurrentState();
        var fromPosition = state?.SafePosition != null 
            ? new SortableUniqueId(state.SafePosition)
            : state?.LastPosition != null 
                ? new SortableUniqueId(state.LastPosition)
                : (SortableUniqueId?)null;

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
            }
        }
    }

    /// <summary>
    ///     Process stream event - pure delegation to orchestrator
    /// </summary>
    internal async Task ProcessStreamEvent(Event evt)
    {
        if (_orchestrator == null)
        {
            await EnsureInitializedAsync();
        }

        if (_orchestrator == null) return;

        // Check if we should process (orchestrator handles duplicate check)
        if (!await _orchestrator.ShouldProcessEventAsync(evt))
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Event {evt.Id} skipped by orchestrator");
            return;
        }

        // Process through orchestrator
        var result = await _orchestrator.ProcessStreamEventAsync(evt);
        if (result.IsSuccess)
        {
            var processResult = result.GetValue();
            if (processResult.RequiresPersistence)
            {
                await PersistStateAsync();
            }
        }
    }

    // Orleans stream observer
    private class StreamObserver : IAsyncObserver<Event>
    {
        private readonly PureInfrastructureMultiProjectionGrain _grain;

        public StreamObserver(PureInfrastructureMultiProjectionGrain grain) => _grain = grain;

        public async Task OnNextAsync(Event item, StreamSequenceToken? token = null)
        {
            await _grain.ProcessStreamEvent(item);
        }

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex)
        {
            _grain._lastError = $"Stream error: {ex.Message}";
            return Task.CompletedTask;
        }
    }

    #region ILifecycleParticipant
    public void Participate(IGrainLifecycle lifecycle)
    {
        lifecycle.Subscribe(
            GetType().FullName!,
            GrainLifecycleStage.Activate + 100,
            InitStreamsAsync,
            CloseStreamsAsync);
    }

    private async Task InitStreamsAsync(CancellationToken ct)
    {
        var projectorName = this.GetPrimaryKeyString();
        var streamInfo = _subscriptionResolver.Resolve(projectorName);
        
        if (streamInfo is not OrleansSekibanStream orleansStream)
        {
            throw new InvalidOperationException($"Invalid stream type: {streamInfo?.GetType().Name}");
        }

        var streamProvider = this.GetStreamProvider(orleansStream.ProviderName);
        _orleansStream = streamProvider.GetStream<Event>(
            StreamId.Create(orleansStream.StreamNamespace, orleansStream.StreamId));

        _orleansStreamHandle = await _orleansStream.SubscribeAsync(new StreamObserver(this), null);
        Console.WriteLine($"[{projectorName}] Subscribed to Orleans stream");
    }

    private Task CloseStreamsAsync(CancellationToken ct) =>
        _orleansStreamHandle?.UnsubscribeAsync() ?? Task.CompletedTask;
    #endregion
}

