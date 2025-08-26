using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     A memory-efficient multi-projection state that manages both safe and unsafe states
///     without duplicating the entire data structure.
/// </summary>
/// <typeparam name="T">The type of the projection that implements IMultiProjector</typeparam>
public record SafeUnsafeMultiProjectionState<T> : ISafeAndUnsafeStateAccessor<T>, IMultiProjectionPayload
    where T : IMultiProjector<T>
{
    private readonly HashSet<Guid> _processedEventIds = new();
    private readonly List<(Event evt, T stateBefore)> _unsafeEvents = new();
    private Guid _lastEventId = Guid.Empty;
    private string _lastSortableUniqueId = string.Empty;
    private T _safeState;
    private T? _unsafeStateCache;
    private bool _unsafeStateCacheValid;
    private int _version;

    public SafeUnsafeMultiProjectionState() => _safeState = T.GenerateInitialPayload();

    public SafeUnsafeMultiProjectionState(T initialState) => _safeState = initialState;

    /// <summary>
    ///     Gets the safe state (events outside the safe window)
    /// </summary>
    public T GetSafeState() => _safeState;

    /// <summary>
    ///     Gets the unsafe state (includes all events)
    /// </summary>
    public T GetUnsafeState()
    {
        if (!_unsafeStateCacheValid)
        {
            _unsafeStateCache = ComputeUnsafeState();
            _unsafeStateCacheValid = true;
        }
        return _unsafeStateCache!;
    }

    /// <summary>
    ///     Process an event and update the state
    /// </summary>
    public ISafeAndUnsafeStateAccessor<T> ProcessEvent(Event evt, SortableUniqueId safeWindowThreshold)
    {
        // Check for duplicate event
        if (_processedEventIds.Contains(evt.Id))
        {
            // Duplicate event - ignore without error
            return this;
        }

        var eventTime = new SortableUniqueId(evt.SortableUniqueIdValue);
        // Tags need to be provided as proper ITag instances - parsing should happen at actor level
        var tags = new List<ITag>();

        // Mark processed
        _processedEventIds.Add(evt.Id);
        _lastEventId = evt.Id;
        _lastSortableUniqueId = evt.SortableUniqueIdValue;
        _version++;

        // Invalidate cache
        _unsafeStateCacheValid = false;

        // Check if event is safe (outside safe window)
        if (eventTime.IsEarlierThanOrEqual(safeWindowThreshold))
        {
            // Safe event - apply directly to safe state
            var result = T.Project(_safeState, evt, tags);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to project event: {result.GetException()}");
            }

            _safeState = result.GetValue();

            // Remove from tracking once safe
            _processedEventIds.Remove(evt.Id);

            // Process any unsafe events that are now safe
            ProcessUnsafeEventsToSafe(safeWindowThreshold, tags);
        } else
        {
            // Unsafe event - add to buffer
            var currentState = GetUnsafeState();
            _unsafeEvents.Add((evt, currentState));
        }

        return this;
    }

    public Guid GetLastEventId() => _lastEventId;
    public string GetLastSortableUniqueId() => _lastSortableUniqueId;
    public int GetVersion() => _version;

    private void ProcessUnsafeEventsToSafe(SortableUniqueId safeWindowThreshold, List<ITag> tags)
    {
        var eventsToPromote = new List<(Event evt, T stateBefore)>();
        var eventsToKeep = new List<(Event evt, T stateBefore)>();

        foreach (var item in _unsafeEvents)
        {
            var eventTime = new SortableUniqueId(item.evt.SortableUniqueIdValue);
            if (eventTime.IsEarlierThanOrEqual(safeWindowThreshold))
            {
                eventsToPromote.Add(item);
            } else
            {
                eventsToKeep.Add(item);
            }
        }

        // Apply promoted events to safe state
        foreach (var item in eventsToPromote.OrderBy(e => e.evt.SortableUniqueIdValue))
        {
            var result = T.Project(_safeState, item.evt, tags);
            if (result.IsSuccess)
            {
                _safeState = result.GetValue();
                // Remove from tracking once safe
                _processedEventIds.Remove(item.evt.Id);
            }
        }

        // Keep only unsafe events
        _unsafeEvents.Clear();
        _unsafeEvents.AddRange(eventsToKeep);

        // Invalidate cache
        _unsafeStateCacheValid = false;
    }

    private T ComputeUnsafeState()
    {
        var state = _safeState;

        // Apply all unsafe events in order
        foreach (var item in _unsafeEvents.OrderBy(e => e.evt.SortableUniqueIdValue))
        {
            // Tags need to be provided as proper ITag instances - parsing should happen at actor level
            var tags = new List<ITag>();
            var result = T.Project(state, item.evt, tags);
            if (result.IsSuccess)
            {
                state = result.GetValue();
            }
        }

        return state;
    }
}
