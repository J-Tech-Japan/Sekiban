using Orleans.Streams;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.MultiProjections;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Refactored Orleans grain implementation for multi-projection using orchestrator pattern
/// </summary>
public class MultiProjectionGrainRefactored : Grain, IMultiProjectionGrain, ILifecycleParticipant<IGrainLifecycle>
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly TimeSpan _fallbackCheckInterval = TimeSpan.FromSeconds(30);
    private readonly int _maxStateSize = 2 * 1024 * 1024; // 2MB default limit
    private readonly int _persistBatchSize = 100; // Batch events before persisting

    // Configuration
    private readonly TimeSpan _persistInterval = TimeSpan.FromMinutes(5);
    private readonly IPersistentState<MultiProjectionGrainState> _state;
    private readonly IEventSubscriptionResolver _subscriptionResolver;
    private long _eventsProcessed;
    private int _eventsSinceLastPersist;
    private IDisposable? _fallbackTimer;

    // State management
    private bool _isInitialized;
    private bool _isSubscriptionActive;
    private string? _lastError;
    private DateTime? _lastEventTime;
    private DateTime _lastPersistTime;
    private IAsyncStream<Event>? _orleansStream;

    // Orleans stream components
    private StreamSubscriptionHandle<Event>? _orleansStreamHandle;

    // Timers
    private IDisposable? _persistTimer;

    // Core orchestrator component
    private IProjectionOrchestrator? _orchestrator;
    private IProjectionPersistenceStore? _persistenceStore;

    public MultiProjectionGrainRefactored(
        [PersistentState("multiProjection", "OrleansStorage")] IPersistentState<MultiProjectionGrainState> state,
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IEventSubscriptionResolver? subscriptionResolver = null,
        IProjectionOrchestrator? orchestrator = null,
        IProjectionPersistenceStore? persistenceStore = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _subscriptionResolver = subscriptionResolver ?? new DefaultOrleansEventSubscriptionResolver();
        _orchestrator = orchestrator;
        _persistenceStore = persistenceStore;
    }

    public async Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();

        if (_orchestrator == null)
        {
            return ResultBox.Error<MultiProjectionState>(
                new InvalidOperationException("Projection orchestrator not initialized"));
        }

        var state = _orchestrator.GetCurrentState();
        if (state == null)
        {
            return ResultBox.Error<MultiProjectionState>(
                new InvalidOperationException("No projection state available"));
        }

        return canGetUnsafeState ? state.GetUnsafeState() : state.GetSafeState();
    }

    public async Task<ResultBox<SerializableMultiProjectionStateDto>> GetSerializableStateAsync(
        bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();

        if (_orchestrator == null)
        {
            return ResultBox.Error<SerializableMultiProjectionStateDto>(
                new InvalidOperationException("Projection orchestrator not initialized"));
        }

        var state = _orchestrator.GetCurrentState();
        if (state == null)
        {
            return ResultBox.Error<SerializableMultiProjectionStateDto>(
                new InvalidOperationException("No projection state available"));
        }

        var serializable = canGetUnsafeState ? state.SerializedUnsafeState : state.SerializedSafeState;
        if (serializable == null)
        {
            return ResultBox.Error<SerializableMultiProjectionStateDto>(
                new InvalidOperationException("No serializable state available"));
        }

        var dto = SerializableMultiProjectionStateDto.FromCore(serializable);
        return ResultBox.FromValue(dto);
    }

    public async Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true)
    {
        await EnsureInitializedAsync();

        if (_orchestrator == null)
        {
            throw new InvalidOperationException("Projection orchestrator not initialized");
        }

        var context = new ProcessingContext(
            canGetUnsafeState: true,
            finishedCatchUp: finishedCatchUp,
            persistenceRequired: finishedCatchUp || _eventsSinceLastPersist + events.Count >= _persistBatchSize);

        var result = await _orchestrator.ProcessEventsAsync(events, context);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to process events: {result.GetException()?.Message}");
        }

        var processResult = result.GetValue();
        _eventsProcessed += processResult.EventsProcessed;

        if (processResult.EventsProcessed > 0)
        {
            _lastEventTime = DateTime.UtcNow;
            _eventsSinceLastPersist += processResult.EventsProcessed;

            // Persist if recommended by orchestrator or batch size reached
            if (processResult.RequiresPersistence || _eventsSinceLastPersist >= _persistBatchSize || finishedCatchUp)
            {
                await PersistStateAsync();
                _eventsSinceLastPersist = 0;
            }
        }
    }

    public async Task<MultiProjectionGrainStatus> GetStatusAsync()
    {
        var currentPosition = _state.State?.LastPosition;
        var isCaughtUp = _isSubscriptionActive;

        // Calculate state size
        long stateSize = 0;
        if (_orchestrator != null)
        {
            var state = _orchestrator.GetCurrentState();
            if (state != null && state.SerializedUnsafeState != null)
            {
                var json = JsonSerializer.Serialize(state.SerializedUnsafeState, _domainTypes.JsonSerializerOptions);
                stateSize = Encoding.UTF8.GetByteCount(json);
            }
        }

        return new MultiProjectionGrainStatus(
            this.GetPrimaryKeyString(),
            _isSubscriptionActive,
            isCaughtUp,
            currentPosition,
            _eventsProcessed,
            _lastEventTime,
            _lastPersistTime,
            stateSize,
            _lastError != null,
            _lastError);
    }

    public async Task<ResultBox<bool>> PersistStateAsync()
    {
        try
        {
            if (_orchestrator == null)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("Projection orchestrator not initialized"));
            }

            var state = _orchestrator.GetCurrentState();
            if (state == null)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("No projection state available"));
            }

            // Use safe state for persistence
            var serializableState = state.SerializedSafeState;
            if (serializableState == null)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("No serializable safe state available"));
            }

            // Serialize to check size
            var json = JsonSerializer.Serialize(serializableState, _domainTypes.JsonSerializerOptions);
            var stateSize = Encoding.UTF8.GetByteCount(json);

            // Check size limit
            if (stateSize > _maxStateSize)
            {
                _lastError = $"State size {stateSize} exceeds limit {_maxStateSize}, skipping persist";
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] WARNING: State size {stateSize} exceeds limit {_maxStateSize}, skipping persist for CosmosDB");
                return ResultBox.FromValue(false);
            }

            var safePosition = state.SafePosition;
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Persisting state: Size={stateSize}, Events={_eventsProcessed}, SafePosition={safePosition}");

            // Update grain state
            _state.State.ProjectorName = this.GetPrimaryKeyString();
            _state.State.SerializedState = json;
            _state.State.LastPersistTime = DateTime.UtcNow;
            _state.State.EventsProcessed = _eventsProcessed;
            _state.State.StateSize = stateSize;
            _state.State.SafeLastPosition = safePosition;

            // Persist to storage
            await _state.WriteStateAsync();

            // Also persist through orchestrator's store if available
            if (_persistenceStore != null)
            {
                await _persistenceStore.SaveAsync(this.GetPrimaryKeyString(), new SerializedProjectionState
                {
                    PayloadJson = json,
                    LastPosition = state.LastPosition,
                    SafePosition = safePosition,
                    Version = state.Version,
                    EventsProcessed = _eventsProcessed
                });
            }

            _lastPersistTime = DateTime.UtcNow;
            _lastError = null;
            _eventsSinceLastPersist = 0;

            return ResultBox.FromValue(true);
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
        await StartSubscriptionInternalAsync();
    }

    public async Task<QueryResultGeneral> ExecuteQueryAsync(IQueryCommon query)
    {
        await EnsureInitializedAsync();

        if (_orchestrator == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: Projection orchestrator is null");
            return new QueryResultGeneral(null!, string.Empty, query);
        }

        try
        {
            var state = _orchestrator.GetCurrentState();
            if (state == null)
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: No state available");
                return new QueryResultGeneral(null!, string.Empty, query);
            }

            var stateResult = state.GetUnsafeState();
            if (!stateResult.IsSuccess)
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: Failed to get state - {stateResult.GetException()?.Message}");
                return new QueryResultGeneral(null!, string.Empty, query);
            }

            var projectionState = stateResult.GetValue();
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: Got state - Version={projectionState.Version}, IsCaughtUp={projectionState.IsCatchedUp}");

            // Create a provider function that returns the payload
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(projectionState.Payload!));

            // Get service provider from grain context
            var serviceProvider = ServiceProvider;

            // Execute the query using QueryTypes
            var result = await _domainTypes.QueryTypes.ExecuteQueryAsync(query, projectorProvider, serviceProvider);

            if (result.IsSuccess)
            {
                var value = result.GetValue();
                return new QueryResultGeneral(value, value?.GetType().FullName ?? string.Empty, query);
            }

            return new QueryResultGeneral(null!, string.Empty, query);
        }
        catch (Exception ex)
        {
            _lastError = $"Query execution failed: {ex.Message}";
            return new QueryResultGeneral(null!, string.Empty, query);
        }
    }

    public async Task<ListQueryResultGeneral> ExecuteListQueryAsync(IListQueryCommon query)
    {
        await EnsureInitializedAsync();

        // If no events have been processed yet, try to refresh from event store
        if (_eventsProcessed == 0)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] No events processed yet, refreshing from event store");
            await RefreshAsync();
        }

        if (_orchestrator == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Orchestrator is null");
            return ListQueryResultGeneral.Empty;
        }

        try
        {
            var state = _orchestrator.GetCurrentState();
            if (state == null)
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] No state available");
                return ListQueryResultGeneral.Empty;
            }

            var stateResult = state.GetUnsafeState();
            if (!stateResult.IsSuccess)
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Failed to get state: {stateResult.GetException()?.Message}");
                return ListQueryResultGeneral.Empty;
            }

            var projectionState = stateResult.GetValue();
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] State retrieved. Payload type: {projectionState.Payload?.GetType().Name}, Events processed: {_eventsProcessed}");

            // Create a provider function that returns the payload
            var projectorProvider = () =>
            {
                if (projectionState.Payload == null)
                {
                    return Task.FromResult(
                        ResultBox.Error<IMultiProjectionPayload>(
                            new InvalidOperationException("Projection state payload is null")));
                }
                return Task.FromResult(ResultBox.FromValue(projectionState.Payload));
            };

            // Get service provider from grain context
            var serviceProvider = ServiceProvider;

            // Execute the list query
            var result = await _domainTypes.QueryTypes.ExecuteListQueryAsGeneralAsync(
                query,
                projectorProvider,
                serviceProvider);

            if (result.IsSuccess)
            {
                return result.GetValue();
            }

            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Query execution failed: {result.GetException()?.Message}");
            return ListQueryResultGeneral.Empty;
        }
        catch (Exception ex)
        {
            _lastError = $"List query execution failed: {ex.Message}";
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Exception in ExecuteListQueryAsync: {ex}");
            return ListQueryResultGeneral.Empty;
        }
    }

    public async Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        await EnsureInitializedAsync();

        if (_orchestrator == null)
        {
            return false;
        }

        var state = _orchestrator.GetCurrentState();
        if (state == null)
        {
            return false;
        }

        return state.ProcessedEventIds.Contains(sortableUniqueId);
    }

    public async Task RefreshAsync()
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] RefreshAsync called - manually catching up from event store");

        await EnsureInitializedAsync();

        if (_orchestrator == null || _eventStore == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Cannot refresh - missing components");
            return;
        }

        try
        {
            var state = _orchestrator.GetCurrentState();
            SortableUniqueId? fromPosition = null;

            if (state != null && !string.IsNullOrEmpty(state.LastPosition))
            {
                fromPosition = new SortableUniqueId(state.LastPosition);
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Current position: {fromPosition.Value}");
            }

            // Load new events from the event store
            var eventsResult = fromPosition == null
                ? await _eventStore.ReadAllEventsAsync(since: null)
                : await _eventStore.ReadAllEventsAsync(since: fromPosition.Value);

            if (eventsResult.IsSuccess)
            {
                var newEvents = eventsResult.GetValue().ToList();
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Found {newEvents.Count} events in event store");

                if (newEvents.Any())
                {
                    // Process the new events through orchestrator
                    var context = new ProcessingContext(
                        canGetUnsafeState: true,
                        finishedCatchUp: true,
                        persistenceRequired: true);

                    var result = await _orchestrator.ProcessEventsAsync(newEvents, context);
                    if (result.IsSuccess)
                    {
                        var processResult = result.GetValue();
                        _eventsProcessed += processResult.EventsProcessed;
                        _lastEventTime = DateTime.UtcNow;

                        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Processed {processResult.EventsProcessed} new events, total: {_eventsProcessed}");

                        // Persist the updated state
                        await PersistStateAsync();
                    }
                }
                else
                {
                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] No new events to process");
                }
            }
            else
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Failed to read events: {eventsResult.GetException()?.Message}");
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Refresh failed: {ex.Message}";
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Refresh error: {ex}");
        }
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var projectorName = this.GetPrimaryKeyString();
        Console.WriteLine($"[MultiProjectionGrainRefactored] OnActivateAsync for {projectorName}");

        // Create orchestrator if not injected
        if (_orchestrator == null)
        {
            Console.WriteLine($"[MultiProjectionGrainRefactored-{projectorName}] Creating new orchestrator");
            
            // Create persistence store adapter for Orleans
            _persistenceStore = new OrleansPersistenceStoreAdapter(_state);
            
            // Create the orchestrator
            _orchestrator = new DefaultProjectionOrchestrator(_domainTypes, _persistenceStore);
            
            // Initialize with any persisted state
            SerializedProjectionState? persistedState = null;
            if (_state.State != null && !string.IsNullOrEmpty(_state.State.SerializedState))
            {
                try
                {
                    Console.WriteLine($"[MultiProjectionGrainRefactored-{projectorName}] Found persisted state in storage");
                    persistedState = new SerializedProjectionState
                    {
                        PayloadJson = _state.State.SerializedState,
                        LastPosition = _state.State.LastPosition,
                        SafePosition = _state.State.SafeLastPosition,
                        EventsProcessed = _state.State.EventsProcessed
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MultiProjectionGrainRefactored-{projectorName}] Failed to restore state: {ex.Message}");
                }
            }
            
            // Initialize orchestrator
            var initResult = await _orchestrator.InitializeAsync(projectorName, persistedState);
            if (!initResult.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to initialize orchestrator: {initResult.GetException()?.Message}");
            }
            
            var projectionState = initResult.GetValue();
            _eventsProcessed = projectionState.EventsProcessed;
            
            Console.WriteLine($"[MultiProjectionGrainRefactored-{projectorName}] Orchestrator initialized - Events processed: {_eventsProcessed}");
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[MultiProjectionGrainRefactored-{this.GetPrimaryKeyString()}] DEACTIVATING - Reason: {reason}, Events processed: {_eventsProcessed}");

        // Persist state before deactivation
        await PersistStateAsync();

        // Clean up resources
        await StopSubscriptionInternalAsync();

        // Unsubscribe from Orleans stream
        if (_orleansStreamHandle != null)
        {
            try
            {
                await _orleansStreamHandle.UnsubscribeAsync();
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Unsubscribed from Orleans stream");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Error unsubscribing from stream: {ex.Message}");
            }
        }

        _persistTimer?.Dispose();
        _fallbackTimer?.Dispose();

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized)
            return;

        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Initializing grain for the first time");
        _isInitialized = true;

        // Set up periodic persistence timer
        if (_persistTimer == null)
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

        // Set up fallback timer
        if (_fallbackTimer == null)
        {
            _fallbackTimer = this.RegisterGrainTimer(
                async () => await FallbackEventCheckAsync(),
                new GrainTimerCreationOptions
                {
                    DueTime = _fallbackCheckInterval,
                    Period = _fallbackCheckInterval,
                    Interleave = true
                });
        }

        await Task.CompletedTask;
    }

    private async Task StartSubscriptionInternalAsync()
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] StartSubscriptionInternalAsync called");

        if (_isSubscriptionActive || _orchestrator == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Skipping subscription start - already active or missing components");
            return;
        }

        try
        {
            // Get starting position from orchestrator state
            var state = _orchestrator.GetCurrentState();
            SortableUniqueId? fromPosition = null;

            if (state != null && !string.IsNullOrEmpty(state.SafePosition))
            {
                fromPosition = new SortableUniqueId(state.SafePosition);
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Resuming from SAFE position: {fromPosition.Value}");
            }
            else if (state != null && !string.IsNullOrEmpty(state.LastPosition))
            {
                fromPosition = new SortableUniqueId(state.LastPosition);
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Resuming from last position: {fromPosition.Value}");
            }
            else
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Starting from beginning (no saved positions)");
            }

            // Catch up from event store
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Catching up from event store");
            var eventsResult = fromPosition == null
                ? await _eventStore.ReadAllEventsAsync(since: null)
                : await _eventStore.ReadAllEventsAsync(since: fromPosition.Value);

            if (eventsResult.IsSuccess)
            {
                var events = eventsResult.GetValue().ToList();
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Found {events.Count} events to catch up");

                if (events.Any())
                {
                    var context = new ProcessingContext(
                        canGetUnsafeState: true,
                        finishedCatchUp: true,
                        persistenceRequired: true);

                    var result = await _orchestrator.ProcessEventsAsync(events, context);
                    if (result.IsSuccess)
                    {
                        var processResult = result.GetValue();
                        _eventsProcessed += processResult.EventsProcessed;
                        _lastEventTime = DateTime.UtcNow;

                        // Update positions in state
                        var currentState = _orchestrator.GetCurrentState();
                        if (currentState != null)
                        {
                            _state.State.LastPosition = currentState.LastPosition;
                            _state.State.SafeLastPosition = currentState.SafePosition;
                        }

                        // Persist state after catchup
                        await PersistStateAsync();

                        Console.WriteLine($"[{this.GetPrimaryKeyString()}] Catchup complete, processed {processResult.EventsProcessed} events");
                    }
                }
            }
            else
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Failed to read events from store: {eventsResult.GetException()?.Message}");
            }

            _isSubscriptionActive = true;
            _lastError = null;
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Subscription started successfully, now receiving live events from Orleans stream");
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to start subscription: {ex.Message}";
            _isSubscriptionActive = false;
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Failed to start subscription: {ex}");
            throw;
        }
    }

    private async Task StopSubscriptionInternalAsync()
    {
        _isSubscriptionActive = false;
        await Task.CompletedTask;
    }

    private async Task FallbackEventCheckAsync()
    {
        // Only run fallback if we haven't received events via stream recently
        if (_lastEventTime == null || DateTime.UtcNow - _lastEventTime > TimeSpan.FromMinutes(1))
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Fallback: No stream events for over 1 minute, checking event store");
            await RefreshAsync();
        }
    }

    /// <summary>
    ///     Process an event received from the Orleans stream
    /// </summary>
    internal async Task ProcessStreamEvent(Event evt)
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] ProcessStreamEvent: {evt.EventType}, SortableUniqueId: {evt.SortableUniqueIdValue}");

        if (!_isInitialized || _orchestrator == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Grain not ready to process events, initializing first");
            await EnsureInitializedAsync();
        }

        if (_orchestrator == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Orchestrator is null, cannot process event");
            return;
        }

        try
        {
            // Check if already processed
            var state = _orchestrator.GetCurrentState();
            if (state != null && state.ProcessedEventIds.Contains(evt.Id.ToString()))
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Event {evt.Id} already processed, skipping");
                return;
            }

            // Process through orchestrator
            var context = new StreamContext(
                isLiveStream: true,
                streamId: $"{this.GetPrimaryKeyString()}_stream");

            var result = await _orchestrator.ProcessStreamEventAsync(evt, context);
            if (result.IsSuccess)
            {
                var processResult = result.GetValue();
                _eventsProcessed += processResult.EventsProcessed;
                _lastEventTime = DateTime.UtcNow;
                _eventsSinceLastPersist += processResult.EventsProcessed;

                // Update position
                _state.State.LastPosition = evt.SortableUniqueIdValue;

                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Event processed successfully, total: {_eventsProcessed}");

                // Persist state after batch
                if (_eventsSinceLastPersist >= _persistBatchSize)
                {
                    await PersistStateAsync();
                    _eventsSinceLastPersist = 0;
                }
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to process stream event: {ex.Message}";
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Error processing stream event: {ex}");
        }
    }

    /// <summary>
    ///     Observer class for receiving events from Orleans streams
    /// </summary>
    private class GrainEventObserver : IAsyncObserver<Event>
    {
        private readonly MultiProjectionGrainRefactored _grain;

        public GrainEventObserver(MultiProjectionGrainRefactored grain) => _grain = grain;

        public async Task OnNextAsync(Event item, StreamSequenceToken? token = null)
        {
            Console.WriteLine($"[GrainEventObserver-{_grain.GetPrimaryKeyString()}] Received event {item.EventType}, ID: {item.Id}");
            await _grain.ProcessStreamEvent(item);
        }

        public Task OnCompletedAsync()
        {
            Console.WriteLine($"[GrainEventObserver-{_grain.GetPrimaryKeyString()}] Stream completed");
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            Console.WriteLine($"[GrainEventObserver-{_grain.GetPrimaryKeyString()}] Stream error: {ex}");
            _grain._lastError = $"Stream error: {ex.Message}";
            return Task.CompletedTask;
        }
    }

    #region ILifecycleParticipant implementation
    public void Participate(IGrainLifecycle lifecycle)
    {
        Console.WriteLine("[MultiProjectionGrainRefactored] Participate called - registering lifecycle stage");
        var stage = GrainLifecycleStage.Activate + 100;
        lifecycle.Subscribe(GetType().FullName!, stage, InitStreamsAsync, CloseStreamsAsync);
        Console.WriteLine($"[MultiProjectionGrainRefactored] Lifecycle stage registered at {stage}");
    }

    private async Task InitStreamsAsync(CancellationToken ct)
    {
        var projectorName = this.GetPrimaryKeyString();
        Console.WriteLine($"[MultiProjectionGrainRefactored-{projectorName}] InitStreamsAsync called in lifecycle stage");

        // Use the resolver to determine which stream to subscribe to
        var streamInfo = _subscriptionResolver.Resolve(projectorName);
        if (streamInfo is not OrleansSekibanStream orleansStream)
        {
            throw new InvalidOperationException($"Expected OrleansSekibanStream but got {streamInfo?.GetType().Name}");
        }

        // Subscribe to the Orleans stream
        var streamProvider = this.GetStreamProvider(orleansStream.ProviderName);
        _orleansStream = streamProvider.GetStream<Event>(StreamId.Create(orleansStream.StreamNamespace, orleansStream.StreamId));

        try
        {
            Console.WriteLine($"[MultiProjectionGrainRefactored-{projectorName}] Getting stream for {orleansStream.StreamNamespace}/{orleansStream.StreamId}");

            // Create observer
            var observer = new GrainEventObserver(this);
            if (_orleansStream == null)
            {
                throw new InvalidOperationException("Stream provider returned null stream instance");
            }
            _orleansStreamHandle = await _orleansStream.SubscribeAsync(observer, null);

            Console.WriteLine($"[MultiProjectionGrainRefactored-{projectorName}] Successfully subscribed to Orleans stream!");
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MultiProjectionGrainRefactored-{projectorName}] ERROR: Failed to subscribe to Orleans stream: {ex}");
            _lastError = $"Stream subscription failed: {ex.Message}";
        }
    }

    private Task CloseStreamsAsync(CancellationToken ct) =>
        _orleansStreamHandle?.UnsubscribeAsync() ?? Task.CompletedTask;
    #endregion
}

/// <summary>
///     Adapter to use Orleans persistent state as persistence store
/// </summary>
internal class OrleansPersistenceStoreAdapter : IProjectionPersistenceStore
{
    private readonly IPersistentState<MultiProjectionGrainState> _state;

    public OrleansPersistenceStoreAdapter(IPersistentState<MultiProjectionGrainState> state)
    {
        _state = state;
    }

    public Task<SerializedProjectionState?> LoadAsync(string projectorName)
    {
        if (_state.State == null || string.IsNullOrEmpty(_state.State.SerializedState))
        {
            return Task.FromResult<SerializedProjectionState?>(null);
        }

        return Task.FromResult<SerializedProjectionState?>(new SerializedProjectionState
        {
            PayloadJson = _state.State.SerializedState,
            LastPosition = _state.State.LastPosition,
            SafePosition = _state.State.SafeLastPosition,
            EventsProcessed = _state.State.EventsProcessed,
            Version = _state.State.Version ?? 0
        });
    }

    public async Task<bool> SaveAsync(string projectorName, SerializedProjectionState state)
    {
        _state.State.ProjectorName = projectorName;
        _state.State.SerializedState = state.PayloadJson;
        _state.State.LastPosition = state.LastPosition;
        _state.State.SafeLastPosition = state.SafePosition;
        _state.State.EventsProcessed = state.EventsProcessed;
        _state.State.Version = state.Version;
        _state.State.LastPersistTime = DateTime.UtcNow;

        await _state.WriteStateAsync();
        return true;
    }
}