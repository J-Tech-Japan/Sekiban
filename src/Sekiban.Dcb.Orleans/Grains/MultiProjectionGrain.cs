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
///     Orleans grain implementation for multi-projection
/// </summary>
public class MultiProjectionGrain : Grain, IMultiProjectionGrain, ILifecycleParticipant<IGrainLifecycle>
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

    // Core components
    private GeneralMultiProjectionActor? _projectionActor;

    public MultiProjectionGrain(
        [PersistentState("multiProjection", "OrleansStorage")] IPersistentState<MultiProjectionGrainState> state,
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IEventSubscriptionResolver? subscriptionResolver = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        // Use provided resolver or fall back to default
        _subscriptionResolver = subscriptionResolver ?? new DefaultOrleansEventSubscriptionResolver();
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

    public async Task<ResultBox<SerializableMultiProjectionStateDto>> GetSerializableStateAsync(
        bool canGetUnsafeState = true)
    {
        await EnsureInitializedAsync();

        if (_projectionActor == null)
        {
            return ResultBox.Error<SerializableMultiProjectionStateDto>(
                new InvalidOperationException("Projection actor not initialized"));
        }

        var rb = await _projectionActor.GetSerializableStateAsync(canGetUnsafeState);
        if (!rb.IsSuccess) return ResultBox.Error<SerializableMultiProjectionStateDto>(rb.GetException());
        var dto = SerializableMultiProjectionStateDto.FromCore(rb.GetValue());
        return ResultBox.FromValue(dto);
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
            _eventsSinceLastPersist += events.Count;

            // Persist state after processing a batch of events or if caught up
            // This balances between data safety and performance
            if (_eventsSinceLastPersist >= _persistBatchSize || finishedCatchUp)
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
        if (_projectionActor != null)
        {
            var serializableState = await _projectionActor.GetSerializableStateAsync();
            if (serializableState.IsSuccess)
            {
                var json = JsonSerializer.Serialize(serializableState.GetValue(), _domainTypes.JsonSerializerOptions);
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
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] WARNING: State size {stateSize} exceeds limit {_maxStateSize}, skipping persist for CosmosDB");
                return ResultBox.FromValue(false); // Return false to indicate skipped
            }
            
            var safePosition = await _projectionActor.GetSafeLastSortableUniqueIdAsync();
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Persisting state: Size={stateSize}, Events={_eventsProcessed}, SafePosition={safePosition}");

            // Update grain state
            _state.State.ProjectorName = this.GetPrimaryKeyString();
            _state.State.SerializedState = json;
            // Position is updated in ProcessStreamEvent when events are processed
            _state.State.LastPersistTime = DateTime.UtcNow;
            _state.State.EventsProcessed = _eventsProcessed;
            _state.State.StateSize = stateSize;
            // Persist safe last position separately from latest processed position
            _state.State.SafeLastPosition = safePosition;

            // Persist to storage
            await _state.WriteStateAsync();

            _lastPersistTime = DateTime.UtcNow;
            _lastError = null; // Clear error on successful persist
            _eventsSinceLastPersist = 0; // Reset counter after successful persist

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
        await StartSubscriptionInternalAsync();
    }

    public async Task<QueryResultGeneral> ExecuteQueryAsync(IQueryCommon query)
    {
        await EnsureInitializedAsync();

        if (_projectionActor == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: Projection actor is null");
            return new QueryResultGeneral(null!, string.Empty, query);
        }

        try
        {
            // Get the current state
            var stateResult = await _projectionActor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                Console.WriteLine(
                    $"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: Failed to get state - {stateResult.GetException()?.Message}");
                return new QueryResultGeneral(null!, string.Empty, query);
            }

            var state = stateResult.GetValue();
            Console.WriteLine(
                $"[{this.GetPrimaryKeyString()}] ExecuteQueryAsync: Got state - Version={state.Version}, IsCaughtUp={state.IsCatchedUp}, LastEventId={state.LastEventId}");

            // Create a provider function that returns the payload
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload!));

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

        if (_projectionActor == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ProjectionActor is null");
            return ListQueryResultGeneral.Empty;
        }

        try
        {
            // Get the current state
            var stateResult = await _projectionActor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                Console.WriteLine(
                    $"[{this.GetPrimaryKeyString()}] Failed to get state: {stateResult.GetException()?.Message}");
                return ListQueryResultGeneral.Empty;
            }

            var state = stateResult.GetValue();
            Console.WriteLine(
                $"[{this.GetPrimaryKeyString()}] State retrieved. Payload type: {state.Payload?.GetType().Name}, Events processed: {_eventsProcessed}");

            // Create a provider function that returns the payload
            var projectorProvider = () =>
            {
                if (state.Payload == null)
                {
                    return Task.FromResult(
                        ResultBox.Error<IMultiProjectionPayload>(
                            new InvalidOperationException("Projection state payload is null")));
                }
                return Task.FromResult(ResultBox.FromValue(state.Payload));
            };

            // Get service provider from grain context
            var serviceProvider = ServiceProvider;

            // Execute the list query using the new method that returns ListQueryResultGeneral directly
            var result = await _domainTypes.QueryTypes.ExecuteListQueryAsGeneralAsync(
                query,
                projectorProvider,
                serviceProvider);

            if (result.IsSuccess)
            {
                return result.GetValue();
            }

            Console.WriteLine(
                $"[{this.GetPrimaryKeyString()}] Query execution failed: {result.GetException()?.Message}");
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

        if (_projectionActor == null)
        {
            return false;
        }

        return await _projectionActor.IsSortableUniqueIdReceived(sortableUniqueId);
    }

    public async Task RefreshAsync()
    {
        Console.WriteLine(
            $"[{this.GetPrimaryKeyString()}] RefreshAsync called - manually catching up from event store");

        await EnsureInitializedAsync();

        if (_projectionActor == null || _eventStore == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Cannot refresh - missing components");
            return;
        }

        try
        {
            // Get current position and check existing state
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
                
                // Log current Weather state if this is WeatherForecastProjection
                if (state.Payload != null && state.Payload.GetType().Name == "WeatherForecastProjection")
                {
                    dynamic weatherProj = state.Payload;
                    var forecasts = weatherProj.GetCurrentForecasts();
                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] BEFORE REFRESH: {forecasts.Count} weather forecasts in state");
                }
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
                    // Process the new events
                    await _projectionActor.AddEventsAsync(newEvents);
                    _eventsProcessed += newEvents.Count;
                    _lastEventTime = DateTime.UtcNow;

                    Console.WriteLine(
                        $"[{this.GetPrimaryKeyString()}] Processed {newEvents.Count} new events, total: {_eventsProcessed}");

                    // Persist the updated state
                    await PersistStateAsync();
                    
                    // Log Weather state after refresh
                    var afterState = await _projectionActor.GetStateAsync();
                    if (afterState.IsSuccess && afterState.GetValue().Payload != null && 
                        afterState.GetValue().Payload.GetType().Name == "WeatherForecastProjection")
                    {
                        dynamic weatherProj = afterState.GetValue().Payload;
                        var forecasts = weatherProj.GetCurrentForecasts();
                        Console.WriteLine($"[{this.GetPrimaryKeyString()}] AFTER REFRESH: {forecasts.Count} weather forecasts in state");
                    }
                } else
                {
                    Console.WriteLine($"[{this.GetPrimaryKeyString()}] No new events to process");
                }
            } else
            {
                Console.WriteLine(
                    $"[{this.GetPrimaryKeyString()}] Failed to read events: {eventsResult.GetException()?.Message}");
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
        // Just prepare basic setup during activation
        // Stream subscription will happen in lifecycle participant
        var projectorName = this.GetPrimaryKeyString();

        Console.WriteLine($"[MultiProjectionGrain] OnActivateAsync for {projectorName}");
        Console.WriteLine(
            $"[MultiProjectionGrain-{projectorName}] Waiting for lifecycle participant to set up stream...");

        // Only create the projection actor if it doesn't exist (first activation)
        if (_projectionActor == null)
        {
            Console.WriteLine($"[MultiProjectionGrain-{projectorName}] Creating new projection actor");
            _projectionActor = new GeneralMultiProjectionActor(
                _domainTypes,
                projectorName,
                new GeneralMultiProjectionActorOptions
                {
                    SafeWindowMs = 20000 // 20 seconds
                });

                // Only restore persisted state on first creation
                if (_state.State != null && !string.IsNullOrEmpty(_state.State.SerializedState))
                {
                    try
                    {
                        Console.WriteLine($"[MultiProjectionGrain-{projectorName}] Restoring persisted state from storage");
                        var deserializedState = JsonSerializer.Deserialize<SerializableMultiProjectionState>(
                            _state.State.SerializedState,
                            _domainTypes.JsonSerializerOptions);

                        if (deserializedState != null && _projectionActor != null)
                        {
                            await _projectionActor.SetCurrentState(deserializedState);
                            _eventsProcessed = _state.State.EventsProcessed;
                            Console.WriteLine(
                                $"[MultiProjectionGrain-{projectorName}] State restored from storage - Events processed: {_eventsProcessed}");
                            
                            // Log Weather state after restoration
                            var restoredState = await _projectionActor.GetStateAsync();
                            if (restoredState.IsSuccess && restoredState.GetValue().Payload != null &&
                                restoredState.GetValue().Payload.GetType().Name == "WeatherForecastProjection")
                            {
                                dynamic weatherProj = restoredState.GetValue().Payload;
                                var forecasts = weatherProj.GetCurrentForecasts();
                                Console.WriteLine($"[MultiProjectionGrain-{projectorName}] RESTORED STATE: {forecasts.Count} weather forecasts");
                                int count = 0;
                                foreach (var forecast in forecasts)
                                {
                                    if (count >= 5) break;
                                    Console.WriteLine($"  - {forecast.Value.Location} on {forecast.Value.Date:yyyy-MM-dd}");
                                    count++;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MultiProjectionGrain-{projectorName}] Failed to restore state: {ex.Message}");
                        // Continue with fresh state
                    }
                } else
                {
                    Console.WriteLine($"[MultiProjectionGrain-{projectorName}] No persisted state to restore");
                }
        } else
        {
            Console.WriteLine($"[MultiProjectionGrain-{projectorName}] Projection actor already exists - keeping in-memory state");
            
            // Log current in-memory state
            var currentState = await _projectionActor.GetStateAsync();
            if (currentState.IsSuccess)
            {
                Console.WriteLine($"[MultiProjectionGrain-{projectorName}] Current in-memory events processed: {_eventsProcessed}");
                if (currentState.GetValue().Payload != null &&
                    currentState.GetValue().Payload.GetType().Name == "WeatherForecastProjection")
                {
                    dynamic weatherProj = currentState.GetValue().Payload;
                    var forecasts = weatherProj.GetCurrentForecasts();
                    Console.WriteLine($"[MultiProjectionGrain-{projectorName}] KEPT IN-MEMORY STATE: {forecasts.Count} weather forecasts");
                }
            }
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[MultiProjectionGrain-{this.GetPrimaryKeyString()}] DEACTIVATING - Reason: {reason}, Events processed: {_eventsProcessed}");
        
        // Log current Weather state before deactivation
        if (_projectionActor != null)
        {
            var currentState = await _projectionActor.GetStateAsync();
            if (currentState.IsSuccess && currentState.GetValue().Payload != null &&
                currentState.GetValue().Payload.GetType().Name == "WeatherForecastProjection")
            {
                dynamic weatherProj = currentState.GetValue().Payload;
                var forecasts = weatherProj.GetCurrentForecasts();
                Console.WriteLine($"[MultiProjectionGrain-{this.GetPrimaryKeyString()}] DEACTIVATING WITH: {forecasts.Count} weather forecasts");
            }
        }
        
        // Persist state before deactivation
        await PersistStateAsync();
        
        // Clean up resources
        await StopSubscriptionInternalAsync();

        // Unsubscribe from Orleans stream if we have a direct subscription
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

        // Set up periodic persistence timer if not already set
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

        // Set up fallback timer to check for new events if stream is not working
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

        if (_isSubscriptionActive || _projectionActor == null)
        {
            Console.WriteLine(
                $"[{this.GetPrimaryKeyString()}] Skipping subscription start - already active or missing components");
            return;
        }

        try
        {
            // Determine starting position from persisted state
            // Use SafeLastPosition as the durable catch-up point to re-apply any windowed events
            SortableUniqueId? fromPosition = null;
            var persisted = _state.State; // state object is injected and should not be null
            if (!string.IsNullOrEmpty(persisted.SafeLastPosition))
            {
                fromPosition = new SortableUniqueId(persisted.SafeLastPosition);
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Resuming from SAFE position: {fromPosition.Value}");
            }
            else if (!string.IsNullOrEmpty(persisted.LastPosition))
            {
                // Fallback to legacy LastPosition if SafeLastPosition not yet stored (backward compatibility)
                fromPosition = new SortableUniqueId(persisted.LastPosition);
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Resuming from legacy position: {fromPosition.Value}");
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
                    await _projectionActor.AddEventsAsync(events);
                    _eventsProcessed += events.Count;
                    _lastEventTime = DateTime.UtcNow;

                    // Update LastPosition (latest processed) to the last event
                    var lastEvent = events.Last();
                    _state.State.LastPosition = lastEvent.SortableUniqueIdValue;
                    // Update SafeLastPosition to current actor safe id after catch-up
                    _state.State.SafeLastPosition = _projectionActor != null ? await _projectionActor.GetSafeLastSortableUniqueIdAsync() : null;

                    // Persist state after catchup
                    await PersistStateAsync();

                    Console.WriteLine(
                        $"[{this.GetPrimaryKeyString()}] Catchup complete, processed {events.Count} events");
                }
            } else
            {
                Console.WriteLine(
                    $"[{this.GetPrimaryKeyString()}] Failed to read events from store: {eventsResult.GetException()?.Message}");
            }

            _isSubscriptionActive = true;
            _lastError = null;
            Console.WriteLine(
                $"[{this.GetPrimaryKeyString()}] Subscription started successfully, now receiving live events from Orleans stream");
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
        await Task.CompletedTask; // Keep async signature
    }

    /// <summary>
    ///     Fallback mechanism to check for new events if stream is not working
    /// </summary>
    private async Task FallbackEventCheckAsync()
    {
        // Only run fallback if we haven't received events via stream recently
        if (_lastEventTime == null || DateTime.UtcNow - _lastEventTime > TimeSpan.FromMinutes(1))
        {
            Console.WriteLine(
                $"[{this.GetPrimaryKeyString()}] Fallback: No stream events for over 1 minute, checking event store");
            await RefreshAsync();
        }
    }

    /// <summary>
    ///     Process an event received from the Orleans stream
    /// </summary>
    internal async Task ProcessStreamEvent(Event evt)
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] ProcessStreamEvent: {evt.EventType}, SortableUniqueId: {evt.SortableUniqueIdValue}");
        
        // Special logging for Weather events
        if (evt.EventType.Contains("Weather"))
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] WEATHER EVENT DETECTED: Type={evt.EventType}, Id={evt.Id}, SortableUniqueId={evt.SortableUniqueIdValue}");
        }

        // Don't call EnsureInitializedAsync here - the grain should already be initialized
        // from the lifecycle participant
        if (!_isInitialized || _projectionActor == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Grain not ready to process events, initializing first");
            await EnsureInitializedAsync();
        }

        if (_projectionActor == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ProjectionActor is null, cannot process event");
            return;
        }

        try
        {
            // Check if we've already processed this event
            if (await _projectionActor.IsSortableUniqueIdReceived(evt.SortableUniqueIdValue))
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] Event {evt.Id} already processed, skipping");
                return;
            }

            // Process the event through the projection actor
            await _projectionActor.AddEventsAsync(new[] { evt }, false);
            
            _eventsProcessed++;
            _lastEventTime = DateTime.UtcNow;
            _eventsSinceLastPersist++;

            // Update position
            _state.State.LastPosition = evt.SortableUniqueIdValue;

            // Special logging for Weather events
            if (evt.EventType.Contains("Weather"))
            {
                Console.WriteLine($"[{this.GetPrimaryKeyString()}] WEATHER EVENT PROCESSED: Events total={_eventsProcessed}, Since persist={_eventsSinceLastPersist}");
            }
            
            Console.WriteLine(
                $"[{this.GetPrimaryKeyString()}] Event processed successfully, total: {_eventsProcessed}");

            // Persist state after batch
            if (_eventsSinceLastPersist >= _persistBatchSize)
            {
                await PersistStateAsync();
                _eventsSinceLastPersist = 0;
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to process stream event: {ex.Message}";
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Error processing stream event: {ex}");
        }
    }

    /// <summary>
    ///     Fallback method for processing events directly (used when DirectOrleansEventSubscription is not available)
    /// </summary>
    internal async Task OnStreamEventReceived(Event evt)
    {
        Console.WriteLine($"[{this.GetPrimaryKeyString()}] OnStreamEventReceived: {evt.EventType}");

        // Ensure the grain is initialized before processing stream events
        if (!_isInitialized)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] Grain not initialized yet, initializing now");
            await EnsureInitializedAsync();
        }

        if (_projectionActor == null)
        {
            Console.WriteLine($"[{this.GetPrimaryKeyString()}] ProjectionActor is null, cannot process event");
            return;
        }

        try
        {
            // Add the event to the projection
            await _projectionActor.AddEventsAsync(new[] { evt });
            _eventsProcessed++;
            _lastEventTime = DateTime.UtcNow;
            _eventsSinceLastPersist++;

            Console.WriteLine(
                $"[{this.GetPrimaryKeyString()}] Event processed successfully, total: {_eventsProcessed}");

            // Persist state after batch or periodically
            if (_eventsSinceLastPersist >= _persistBatchSize)
            {
                await PersistStateAsync();
                _eventsSinceLastPersist = 0;
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
        private readonly MultiProjectionGrain _grain;

        public GrainEventObserver(MultiProjectionGrain grain) => _grain = grain;

        public async Task OnNextAsync(Event item, StreamSequenceToken? token = null)
        {
            Console.WriteLine(
                $"[GrainEventObserver-{_grain.GetPrimaryKeyString()}] Received event {item.EventType}, ID: {item.Id}");

            // Process the event directly through the projection actor
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
    /// <summary>
    ///     Method to participate in the grain lifecycle.
    ///     Registers a custom stage to be executed after the Activate stage.
    /// </summary>
    /// <param name="lifecycle">Grain lifecycle</param>
    public void Participate(IGrainLifecycle lifecycle)
    {
        Console.WriteLine("[MultiProjectionGrain] Participate called - registering lifecycle stage");
        var stage = GrainLifecycleStage.Activate + 100;
        lifecycle.Subscribe(GetType().FullName!, stage, InitStreamsAsync, CloseStreamsAsync);
        Console.WriteLine($"[MultiProjectionGrain] Lifecycle stage registered at {stage}");
    }

    /// <summary>
    ///     Method to initialize the stream.
    ///     Executed after the Activate stage.
    /// </summary>
    private async Task InitStreamsAsync(CancellationToken ct)
    {
        var projectorName = this.GetPrimaryKeyString();
        Console.WriteLine($"[MultiProjectionGrain-{projectorName}] InitStreamsAsync called in lifecycle stage");

        // Use the resolver to determine which stream to subscribe to
        var streamInfo = _subscriptionResolver.Resolve(projectorName);
        if (streamInfo is not OrleansSekibanStream orleansStream)
        {
            throw new InvalidOperationException($"Expected OrleansSekibanStream but got {streamInfo?.GetType().Name}");
        }

        // Subscribe to the Orleans stream
        var streamProvider = this.GetStreamProvider(orleansStream.ProviderName);
        _orleansStream
            = streamProvider.GetStream<Event>(StreamId.Create(orleansStream.StreamNamespace, orleansStream.StreamId));

        try
        {
            Console.WriteLine(
                $"[MultiProjectionGrain-{projectorName}] Getting stream for {orleansStream.StreamNamespace}/{orleansStream.StreamId}");
            Console.WriteLine($"[MultiProjectionGrain-{projectorName}] Stream created: {_orleansStream != null}");

            // Create a simple observer that directly processes events
            var observer = new GrainEventObserver(this);
            if (_orleansStream == null)
            {
                throw new InvalidOperationException("Stream provider returned null stream instance");
            }
            _orleansStreamHandle = await _orleansStream.SubscribeAsync(observer, null);

            Console.WriteLine($"[MultiProjectionGrain-{projectorName}] Successfully subscribed to Orleans stream!");
            Console.WriteLine($"[MultiProjectionGrain-{projectorName}] Stream handle: {_orleansStreamHandle != null}");

            // Initialize the grain without doing a full catchup
            // The catchup will happen in OnActivateAsync
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[MultiProjectionGrain-{projectorName}] ERROR: Failed to subscribe to Orleans stream: {ex}");
            _lastError = $"Stream subscription failed: {ex.Message}";
        }
    }

    /// <summary>
    ///     Method to clean up the stream.
    ///     Executed when the grain is deactivated.
    /// </summary>
    private Task CloseStreamsAsync(CancellationToken ct) =>
        _orleansStreamHandle?.UnsubscribeAsync() ?? Task.CompletedTask;
    #endregion
}
