using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Enhanced orchestrator with all business logic from Grain
/// </summary>
public class EnhancedProjectionOrchestrator : IProjectionOrchestratorV2
{
    private readonly DcbDomainTypes _domainTypes;
    // Persistence store is optional for now
    private GeneralMultiProjectionActor? _projectionActor;
    private ProjectionState? _currentState;
    private OrchestratorConfiguration _config = new();
    
    // Business logic state tracking
    private readonly HashSet<string> _processedEventIds = new();
    private int _eventsSinceLastPersist = 0;
    private DateTime _lastPersistTime = DateTime.UtcNow;
    private DateTime? _lastEventTime;
    private long _totalEventsProcessed = 0;

    public EnhancedProjectionOrchestrator(DcbDomainTypes domainTypes)
    {
        _domainTypes = domainTypes;
    }

    public void Configure(OrchestratorConfiguration config)
    {
        _config = config;
    }

    public async Task<ResultBox<ProjectionState>> InitializeAsync(
        string projectorName,
        SerializedProjectionState? persistedState = null)
    {
        try
        {
            // Create projection actor with safe window configuration
            _projectionActor = new GeneralMultiProjectionActor(
                _domainTypes,
                projectorName,
                new GeneralMultiProjectionActorOptions
                {
                    SafeWindowMs = (int)_config.SafeWindow.TotalMilliseconds
                });

            // Restore state if available
            if (persistedState != null && !string.IsNullOrEmpty(persistedState.PayloadJson))
            {
                var deserializedState = JsonSerializer.Deserialize<SerializableMultiProjectionState>(
                    persistedState.PayloadJson,
                    _domainTypes.JsonSerializerOptions);

                if (deserializedState != null)
                {
                    await _projectionActor.SetCurrentState(deserializedState);
                    _totalEventsProcessed = persistedState.EventsProcessed;
                    _lastPersistTime = DateTime.UtcNow;
                    
                    // Restore processed event IDs from safe position
                    if (!string.IsNullOrEmpty(persistedState.SafePosition))
                    {
                        // In a real implementation, we might need to track these differently
                        // For now, we trust the safe position mechanism
                    }
                }
            }

            // Get initial state
            var stateResult = await _projectionActor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                return ResultBox.Error<ProjectionState>(stateResult.GetException());
            }

            var state = stateResult.GetValue();
            _currentState = new ProjectionState(
                projectorName,
                state.Payload,
                state.LastSortableUniqueId,
                await _projectionActor.GetSafeLastSortableUniqueIdAsync(),
                state.Version,
                _totalEventsProcessed,
                state.IsCatchedUp);

            return ResultBox.FromValue(_currentState);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProjectionState>(ex);
        }
    }

    public async Task<ResultBox<ProcessResultV2>> ProcessEventsAsync(IReadOnlyList<Event> events)
    {
        if (_projectionActor == null)
        {
            return ResultBox.Error<ProcessResultV2>(
                new InvalidOperationException("Orchestrator not initialized"));
        }

        var sw = Stopwatch.StartNew();
        var eventsToProcess = new List<Event>();
        var skippedCount = 0;

        // Business logic: Check for duplicates if enabled
        if (_config.EnableDuplicateCheck)
        {
            foreach (var evt in events)
            {
                if (await ShouldProcessEventAsync(evt))
                {
                    eventsToProcess.Add(evt);
                    _processedEventIds.Add(evt.Id.ToString());
                }
                else
                {
                    skippedCount++;
                }
            }
        }
        else
        {
            eventsToProcess.AddRange(events);
        }

        // Process non-duplicate events
        if (eventsToProcess.Count > 0)
        {
            await _projectionActor.AddEventsAsync(eventsToProcess);
            _totalEventsProcessed += eventsToProcess.Count;
            _eventsSinceLastPersist += eventsToProcess.Count;
            _lastEventTime = DateTime.UtcNow;
        }

        // Get updated state
        var stateResult = await _projectionActor.GetStateAsync();
        if (!stateResult.IsSuccess)
        {
            return ResultBox.Error<ProcessResultV2>(stateResult.GetException());
        }

        var state = stateResult.GetValue();
        var safePosition = await _projectionActor.GetSafeLastSortableUniqueIdAsync();

        // Business logic: Determine if persistence is required
        var persistenceDecision = await DeterminePersistenceAsync(eventsToProcess.Count > 0);

        _currentState = new ProjectionState(
            _currentState?.ProjectorName ?? "Unknown",
            state.Payload,
            state.LastSortableUniqueId,
            safePosition,
            state.Version,
            _totalEventsProcessed,
            state.IsCatchedUp);

        return ResultBox.FromValue(new ProcessResultV2(
            EventsProcessed: eventsToProcess.Count,
            EventsSkipped: skippedCount,
            LastPosition: state.LastSortableUniqueId,
            SafePosition: safePosition,
            RequiresPersistence: persistenceDecision.ShouldPersist,
            PersistReason: persistenceDecision.Reason,
            ProcessingTime: sw.Elapsed));
    }

    public async Task<ResultBox<ProcessResultV2>> ProcessStreamEventAsync(Event evt)
    {
        // Stream events are processed individually with duplicate checking
        return await ProcessEventsAsync(new[] { evt });
    }

    public async Task<bool> ShouldProcessEventAsync(Event evt)
    {
        if (!_config.EnableDuplicateCheck)
        {
            return true;
        }

        // Check if we've already processed this event
        if (_processedEventIds.Contains(evt.Id.ToString()))
        {
            return false;
        }

        // Also check with the projection actor
        if (_projectionActor != null)
        {
            var isReceived = await _projectionActor.IsSortableUniqueIdReceived(evt.SortableUniqueIdValue);
            if (isReceived)
            {
                _processedEventIds.Add(evt.Id.ToString()); // Cache for next time
                return false;
            }
        }

        return true;
    }

    public async Task<PersistenceDecision> ShouldPersistAsync()
    {
        return await DeterminePersistenceAsync(false);
    }

    private async Task<PersistenceDecision> DeterminePersistenceAsync(bool hasNewEvents)
    {
        var timeSinceLastPersist = DateTime.UtcNow - _lastPersistTime;
        
        // Business logic for persistence timing
        if (_eventsSinceLastPersist >= _config.PersistBatchSize)
        {
            return new PersistenceDecision(
                true,
                PersistenceReason.BatchSizeReached,
                _eventsSinceLastPersist,
                timeSinceLastPersist);
        }

        if (timeSinceLastPersist >= _config.PersistInterval)
        {
            return new PersistenceDecision(
                true,
                PersistenceReason.PeriodicCheckpoint,
                _eventsSinceLastPersist,
                timeSinceLastPersist);
        }

        if (hasNewEvents && _currentState?.IsCaughtUp == true)
        {
            return new PersistenceDecision(
                true,
                PersistenceReason.CatchUpComplete,
                _eventsSinceLastPersist,
                timeSinceLastPersist);
        }

        // Check if safe window has passed since last event
        if (_lastEventTime.HasValue)
        {
            var timeSinceLastEvent = DateTime.UtcNow - _lastEventTime.Value;
            if (timeSinceLastEvent > _config.SafeWindow && _eventsSinceLastPersist > 0)
            {
                return new PersistenceDecision(
                    true,
                    PersistenceReason.SafeWindowPassed,
                    _eventsSinceLastPersist,
                    timeSinceLastPersist);
            }
        }

        return new PersistenceDecision(
            false,
            null,
            _eventsSinceLastPersist,
            timeSinceLastPersist);
    }

    public async Task<StateSizeCheck> CheckStateSizeAsync()
    {
        if (_projectionActor == null)
        {
            return new StateSizeCheck(0, _config.MaxStateSize, false, "Orchestrator not initialized");
        }

        var serializableResult = await _projectionActor.GetSerializableStateAsync();
        if (!serializableResult.IsSuccess)
        {
            return new StateSizeCheck(0, _config.MaxStateSize, false, "Failed to get serializable state");
        }

        var serializableState = serializableResult.GetValue();
        var json = JsonSerializer.Serialize(serializableState, _domainTypes.JsonSerializerOptions);
        var stateSize = Encoding.UTF8.GetByteCount(json);

        var exceedsLimit = stateSize > _config.MaxStateSize;
        var warning = exceedsLimit
            ? $"State size {stateSize} exceeds maximum {_config.MaxStateSize}"
            : null;

        return new StateSizeCheck(stateSize, _config.MaxStateSize, exceedsLimit, warning);
    }

    public async Task<ResultBox<SerializedProjectionStateV2>> GetSerializableStateAsync(bool canGetUnsafeState = true)
    {
        if (_projectionActor == null)
        {
            return ResultBox.Error<SerializedProjectionStateV2>(
                new InvalidOperationException("Orchestrator not initialized"));
        }

        var serializableResult = await _projectionActor.GetSerializableStateAsync(canGetUnsafeState);
        if (!serializableResult.IsSuccess)
        {
            return ResultBox.Error<SerializedProjectionStateV2>(serializableResult.GetException());
        }

        var serializableState = serializableResult.GetValue();
        var json = JsonSerializer.Serialize(serializableState, _domainTypes.JsonSerializerOptions);
        var stateSize = Encoding.UTF8.GetByteCount(json);

        // Update persistence tracking when state is retrieved for persistence
        if (canGetUnsafeState)
        {
            _lastPersistTime = DateTime.UtcNow;
            _eventsSinceLastPersist = 0;
        }

        var safePosition = await _projectionActor.GetSafeLastSortableUniqueIdAsync();

        return ResultBox.FromValue(new SerializedProjectionStateV2
        {
            PayloadJson = json,
            LastPosition = _currentState?.LastPosition,
            SafePosition = safePosition,
            EventsProcessed = _totalEventsProcessed,
            Version = _currentState?.Version ?? 0,
            Timestamp = DateTime.UtcNow,
            StateSize = stateSize
        });
    }

    public ProjectionState? GetCurrentState() => _currentState;
}