using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Storage;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Default implementation of projection orchestrator
///     Manages event processing pipeline with complete testability
/// </summary>
public class DefaultProjectionOrchestrator : IProjectionOrchestrator
{
    private readonly GeneralMultiProjectionActor _actor;
    private readonly IEventStore _eventStore;
    private readonly DcbDomainTypes _domainTypes;
    private readonly string _projectorName;
    private readonly TimeSpan _safeWindowDuration;
    private readonly object _stateLock = new();

    // State tracking
    private readonly HashSet<Guid> _processedEventIds = new();
    private PositionInfo _currentPosition = new(null, null, 0);
    private bool _isCaughtUp;

    public DefaultProjectionOrchestrator(
        DcbDomainTypes domainTypes,
        string projectorName,
        IEventStore eventStore,
        TimeSpan? safeWindowDuration = null)
    {
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _projectorName = projectorName ?? throw new ArgumentNullException(nameof(projectorName));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _safeWindowDuration = safeWindowDuration ?? TimeSpan.FromSeconds(20);

        _actor = new GeneralMultiProjectionActor(
            domainTypes,
            projectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = (int)_safeWindowDuration.TotalMilliseconds
            });
    }

    public async Task<ResultBox<ProjectionState>> InitializeAsync(
        string projectorName,
        SerializedProjectionState? persistedState = null)
    {
        try
        {
            if (persistedState != null)
            {
                var restoreResult = await RestoreStateAsync(persistedState);
                if (!restoreResult.IsSuccess)
                {
                    return ResultBox.Error<ProjectionState>(restoreResult.GetException());
                }
            }

            var stateResult = await _actor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                return ResultBox.Error<ProjectionState>(stateResult.GetException());
            }

            var state = stateResult.GetValue();
            return ResultBox.FromValue(new ProjectionState(
                projectorName,
                state.Payload,
                _currentPosition,
                _isCaughtUp));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProjectionState>(ex);
        }
    }

    public async Task<ResultBox<ProcessResult>> ProcessEventsAsync(
        IReadOnlyList<Event> events,
        ProcessingContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var processed = 0;
            string? lastPosition = null;
            string? safePosition = null;
            var safeThreshold = GetSafeWindowThreshold();

            // Filter and prepare events
            var filteredEvents = new List<Event>();
            foreach (var evt in events)
            {
                // Duplicate check
                if (context.CheckDuplicates && !_processedEventIds.Add(evt.Id))
                    continue;

                filteredEvents.Add(evt);
                processed++;
                lastPosition = evt.SortableUniqueIdValue;

                // Update safe position
                if (string.Compare(evt.SortableUniqueIdValue, safeThreshold.Value, StringComparison.Ordinal) <= 0)
                {
                    safePosition = evt.SortableUniqueIdValue;
                }
            }

            if (filteredEvents.Any())
            {
                // Determine if all events are safe
                var allSafe = filteredEvents.All(e =>
                    string.Compare(e.SortableUniqueIdValue, safeThreshold.Value, StringComparison.Ordinal) <= 0);

                // Process through actor
                await _actor.AddEventsAsync(filteredEvents, finishedCatchUp: !context.IsStreaming || allSafe);

                // Update position
                lock (_stateLock)
                {
                    _currentPosition = new PositionInfo(
                        lastPosition ?? _currentPosition.LastPosition,
                        safePosition ?? _currentPosition.SafePosition,
                        _currentPosition.EventsProcessed + processed);

                    if (!context.IsStreaming && allSafe)
                    {
                        _isCaughtUp = true;
                    }
                }
            }

            stopwatch.Stop();
            return ResultBox.FromValue(new ProcessResult(
                processed,
                lastPosition,
                safePosition,
                processed >= context.BatchSize,
                stopwatch.Elapsed));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProcessResult>(ex);
        }
    }

    public async Task<ResultBox<ProcessResult>> ProcessStreamEventAsync(Event evt, StreamContext context)
    {
        try
        {
            // Single event processing for streaming
            var processingContext = new ProcessingContext(
                IsStreaming: true,
                CheckDuplicates: true,
                BatchSize: 1,
                SafeWindow: _safeWindowDuration);

            return await ProcessEventsAsync(new[] { evt }, processingContext);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProcessResult>(ex);
        }
    }

    public async Task<ResultBox<ProjectionState>> GetCurrentStateAsync()
    {
        try
        {
            var stateResult = await _actor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                return ResultBox.Error<ProjectionState>(stateResult.GetException());
            }

            var state = stateResult.GetValue();
            
            lock (_stateLock)
            {
                return ResultBox.FromValue(new ProjectionState(
                    _projectorName,
                    state.Payload,
                    _currentPosition,
                    _isCaughtUp));
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProjectionState>(ex);
        }
    }

    public async Task<ResultBox<SerializedProjectionState>> SerializeStateAsync()
    {
        try
        {
            var stateResult = await _actor.GetSerializableStateAsync();
            if (!stateResult.IsSuccess)
            {
                return ResultBox.Error<SerializedProjectionState>(stateResult.GetException());
            }

            var state = stateResult.GetValue();
            
            lock (_stateLock)
            {
                return ResultBox.FromValue(new SerializedProjectionState(
                    state.Payload,
                    state.MultiProjectionPayloadType,
                    state.ProjectorName,
                    state.ProjectorVersion,
                    _currentPosition,
                    DateTime.UtcNow,
                    _isCaughtUp));
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializedProjectionState>(ex);
        }
    }

    public async Task<ResultBox<bool>> RestoreStateAsync(SerializedProjectionState state)
    {
        try
        {
            // Create SerializableMultiProjectionState for actor
            var serializableState = new SerializableMultiProjectionState(
                state.Payload,
                state.TypeName,
                state.ProjectorName,
                state.Version,
                state.Position.LastPosition ?? string.Empty,
                Guid.Empty,
                (int)state.Position.EventsProcessed,
                state.IsCaughtUp,
                true // IsSafeState when restoring
            );

            await _actor.SetCurrentState(serializableState);

            lock (_stateLock)
            {
                _currentPosition = state.Position;
                _isCaughtUp = state.IsCaughtUp;
                
                // Restore processed event tracking
                _processedEventIds.Clear();
            }

            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public Task<ResultBox<PositionInfo>> GetPositionInfoAsync()
    {
        lock (_stateLock)
        {
            return Task.FromResult(ResultBox.FromValue(_currentPosition));
        }
    }

    public Task<ResultBox<bool>> UpdatePositionAsync(string position, bool isSafe)
    {
        try
        {
            lock (_stateLock)
            {
                _currentPosition = new PositionInfo(
                    position,
                    isSafe ? position : _currentPosition.SafePosition,
                    _currentPosition.EventsProcessed + 1);
            }
            
            return Task.FromResult(ResultBox.FromValue(true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ResultBox.Error<bool>(ex));
        }
    }

    public async Task<bool> IsEventProcessedAsync(string sortableUniqueId)
    {
        return await _actor.IsSortableUniqueIdReceived(sortableUniqueId);
    }

    public async Task<ResultBox<IReadOnlyList<Event>>> LoadEventsFromStoreAsync(
        string? fromPosition = null,
        int batchSize = 1000)
    {
        try
        {
            ResultBox<IEnumerable<Event>> eventsResult;
            
            if (string.IsNullOrEmpty(fromPosition))
            {
                eventsResult = await _eventStore.ReadAllEventsAsync(since: null);
            }
            else
            {
                var position = new SortableUniqueId(fromPosition);
                eventsResult = await _eventStore.ReadAllEventsAsync(since: position);
            }

            if (!eventsResult.IsSuccess)
            {
                return ResultBox.Error<IReadOnlyList<Event>>(eventsResult.GetException());
            }

            var events = eventsResult.GetValue().Take(batchSize).ToList();
            return ResultBox.FromValue<IReadOnlyList<Event>>(events);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IReadOnlyList<Event>>(ex);
        }
    }

    private SortableUniqueId GetSafeWindowThreshold()
    {
        var threshold = DateTime.UtcNow.Subtract(_safeWindowDuration);
        return new SortableUniqueId(SortableUniqueId.Generate(threshold, Guid.Empty));
    }
}