using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Projection state manager V5 without ProjectionRequest that resolves itemId first and applies a global projector
/// </summary>
/// <typeparam name="T">The type of data being projected</typeparam>
public record SafeUnsafeProjectionStateV5<T> where T : class
{
    /// <summary>
    ///     Current data (includes unsafe modifications)
    /// </summary>
    private readonly Dictionary<Guid, T> _currentData = new();

    /// <summary>
    ///     Safe state backup with list of unsafe events per item
    /// </summary>
    private readonly Dictionary<Guid, SafeStateBackup<T>> _safeBackup = new();

    /// <summary>
    ///     SafeWindow threshold
    /// </summary>
    public string SafeWindowThreshold { get; init; } = string.Empty;

    /// <summary>
    ///     Initializes a new instance
    /// </summary>
    public SafeUnsafeProjectionStateV5() { }

    private SafeUnsafeProjectionStateV5(
        Dictionary<Guid, T> currentData,
        Dictionary<Guid, SafeStateBackup<T>> safeBackup,
        string safeWindowThreshold)
    {
        _currentData = new Dictionary<Guid, T>(currentData);
        _safeBackup = new Dictionary<Guid, SafeStateBackup<T>>(safeBackup);
        SafeWindowThreshold = safeWindowThreshold;
    }

    /// <summary>
    ///     Process a single event by resolving target itemId and applying a projector
    /// </summary>
    /// <param name="evt">Event to process</param>
    /// <param name="resolveItemId">Resolver to get the single target itemId; return null to skip</param>
    /// <param name="projector">Global projector of the item state</param>
    /// <returns>New state</returns>
    public SafeUnsafeProjectionStateV5<T> ProcessEvent(
        Event evt,
        Func<Event, Guid?> resolveItemId,
        Func<Guid, T?, Event, T?> projector)
    {
        var id = resolveItemId(evt);
        if (id is null)
        {
            return ProcessNewlySafeEvents(projector);
        }

        return ProcessEvent(evt, _ => new[] { id.Value }, projector);
    }

    /// <summary>
    ///     Process a single event that may target multiple itemIds
    /// </summary>
    /// <param name="evt">Event to process</param>
    /// <param name="resolveItemIds">Resolver to get all target itemIds; empty to skip</param>
    /// <param name="projector">Global projector of the item state</param>
    /// <returns>New state</returns>
    public SafeUnsafeProjectionStateV5<T> ProcessEvent(
        Event evt,
        Func<Event, IEnumerable<Guid>> resolveItemIds,
        Func<Guid, T?, Event, T?> projector)
    {
        var itemIds = resolveItemIds(evt).Distinct().ToList();
        var isEventSafe = string.Compare(evt.SortableUniqueIdValue, SafeWindowThreshold, StringComparison.Ordinal) <= 0;

        var currentState = ProcessNewlySafeEvents(projector);

        if (itemIds.Count == 0)
        {
            return currentState;
        }

        if (isEventSafe)
        {
            return currentState.ProcessSafeProjection(itemIds, evt, projector);
        }

        return currentState.ProcessUnsafeProjection(itemIds, evt, projector);
    }

    /// <summary>
    ///     Process multiple events
    /// </summary>
    /// <param name="events">Events to process</param>
    /// <param name="resolveItemIds">Resolver to get all target itemIds for an event</param>
    /// <param name="projector">Global projector of the item state</param>
    /// <returns>New state</returns>
    public SafeUnsafeProjectionStateV5<T> ProcessEvents(
        IEnumerable<Event> events,
        Func<Event, IEnumerable<Guid>> resolveItemIds,
        Func<Guid, T?, Event, T?> projector)
    {
        var state = this;
        foreach (var evt in events)
        {
            state = state.ProcessEvent(evt, resolveItemIds, projector);
        }
        return state;
    }

    /// <summary>
    ///     Process events that have transitioned from unsafe to safe
    /// </summary>
    private SafeUnsafeProjectionStateV5<T> ProcessNewlySafeEvents(
        Func<Guid, T?, Event, T?> projector)
    {
        var newCurrentData = new Dictionary<Guid, T>(_currentData);
        var newSafeBackup = new Dictionary<Guid, SafeStateBackup<T>>();
        var hasChanges = false;

        foreach (var kvp in _safeBackup)
        {
            var itemId = kvp.Key;
            var backup = kvp.Value;

            var nowSafeEvents = backup
                .UnsafeEvents
                .Where(e => string.Compare(e.SortableUniqueIdValue, SafeWindowThreshold, StringComparison.Ordinal) <= 0)
                .OrderBy(e => e.SortableUniqueIdValue)
                .ToList();

            var stillUnsafeEvents = backup
                .UnsafeEvents
                .Where(e => string.Compare(e.SortableUniqueIdValue, SafeWindowThreshold, StringComparison.Ordinal) > 0)
                .ToList();

            if (nowSafeEvents.Count > 0)
            {
                hasChanges = true;

                var currentItemState = backup.SafeState;

                foreach (var safeEvent in nowSafeEvents)
                {
                    currentItemState = projector(itemId, currentItemState, safeEvent);
                }

                if (stillUnsafeEvents.Count > 0)
                {
                    newSafeBackup[itemId] = new SafeStateBackup<T>(currentItemState!, stillUnsafeEvents);

                    var unsafeState = currentItemState;
                    foreach (var unsafeEvent in stillUnsafeEvents)
                    {
                        unsafeState = projector(itemId, unsafeState, unsafeEvent);
                    }

                    if (unsafeState != null)
                    {
                        newCurrentData[itemId] = unsafeState;
                    }
                    else
                    {
                        newCurrentData.Remove(itemId);
                    }
                }
                else
                {
                    if (currentItemState != null)
                    {
                        newCurrentData[itemId] = currentItemState;
                    }
                    else
                    {
                        newCurrentData.Remove(itemId);
                    }
                }
            }
            else
            {
                newSafeBackup[itemId] = backup;
            }
        }

        if (!hasChanges)
        {
            return this;
        }

        return new SafeUnsafeProjectionStateV5<T>(newCurrentData, newSafeBackup, SafeWindowThreshold);
    }

    /// <summary>
    ///     Process safe projections for itemIds
    /// </summary>
    private SafeUnsafeProjectionStateV5<T> ProcessSafeProjection(
        IEnumerable<Guid> itemIds,
        Event evt,
        Func<Guid, T?, Event, T?> projector)
    {
        var newCurrentData = new Dictionary<Guid, T>(_currentData);
        var newSafeBackup = new Dictionary<Guid, SafeStateBackup<T>>(_safeBackup);

        foreach (var itemId in itemIds)
        {
            if (_safeBackup.TryGetValue(itemId, out var backup))
            {
                var updatedSafeState = projector(itemId, backup.SafeState, evt);

                if (updatedSafeState != null)
                {
                    newSafeBackup[itemId] = backup with { SafeState = updatedSafeState };
                }
                else
                {
                    newSafeBackup.Remove(itemId);
                    if (backup.UnsafeEvents.Count == 0)
                    {
                        newCurrentData.Remove(itemId);
                    }
                }
            }
            else
            {
                var currentValue = newCurrentData.TryGetValue(itemId, out var existing) ? existing : null;
                var newValue = projector(itemId, currentValue, evt);

                if (newValue != null)
                {
                    newCurrentData[itemId] = newValue;
                }
                else
                {
                    newCurrentData.Remove(itemId);
                }
            }
        }

        return new SafeUnsafeProjectionStateV5<T>(newCurrentData, newSafeBackup, SafeWindowThreshold);
    }

    /// <summary>
    ///     Process unsafe projections for itemIds
    /// </summary>
    private SafeUnsafeProjectionStateV5<T> ProcessUnsafeProjection(
        IEnumerable<Guid> itemIds,
        Event evt,
        Func<Guid, T?, Event, T?> projector)
    {
        var newCurrentData = new Dictionary<Guid, T>(_currentData);
        var newSafeBackup = new Dictionary<Guid, SafeStateBackup<T>>(_safeBackup);

        foreach (var itemId in itemIds)
        {
            if (_safeBackup.TryGetValue(itemId, out var existingBackup))
            {
                var updatedEvents = new List<Event>(existingBackup.UnsafeEvents) { evt };
                newSafeBackup[itemId] = existingBackup with { UnsafeEvents = updatedEvents };

                var currentValue = newCurrentData.TryGetValue(itemId, out var existing) ? existing : null;
                var newValue = projector(itemId, currentValue, evt);

                if (newValue != null)
                {
                    newCurrentData[itemId] = newValue;
                }
                else
                {
                    newCurrentData.Remove(itemId);
                    newSafeBackup.Remove(itemId);
                }
            }
            else
            {
                var safeState = newCurrentData.TryGetValue(itemId, out var existing) ? existing : null;
                var newValue = projector(itemId, safeState, evt);

                if (newValue != null)
                {
                    newCurrentData[itemId] = newValue;

                    if (safeState != null)
                    {
                        newSafeBackup[itemId] = new SafeStateBackup<T>(safeState, new List<Event> { evt });
                    }
                    else
                    {
                        newSafeBackup[itemId] = new SafeStateBackup<T>(null!, new List<Event> { evt });
                    }
                }
            }
        }

        return new SafeUnsafeProjectionStateV5<T>(newCurrentData, newSafeBackup, SafeWindowThreshold);
    }

    /// <summary>
    ///     Update SafeWindow threshold and reprocess if needed
    /// </summary>
    public SafeUnsafeProjectionStateV5<T> UpdateSafeWindowThreshold(
        string newThreshold,
        Func<Guid, T?, Event, T?> projector)
    {
        var newState = this with { SafeWindowThreshold = newThreshold };
        return newState.ProcessNewlySafeEvents(projector);
    }

    /// <summary>
    ///     Get current state
    /// </summary>
    public IReadOnlyDictionary<Guid, T> GetCurrentState() => _currentData;

    /// <summary>
    ///     Get safe-only state
    /// </summary>
    public IReadOnlyDictionary<Guid, T> GetSafeState()
    {
        var result = new Dictionary<Guid, T>();

        foreach (var kvp in _currentData)
        {
            if (_safeBackup.TryGetValue(kvp.Key, out var backup))
            {
                if (backup.SafeState != null)
                {
                    result[kvp.Key] = backup.SafeState;
                }
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    /// <summary>
    ///     Check whether specific item has unsafe modifications
    /// </summary>
    public bool IsItemUnsafe(Guid itemId) => _safeBackup.ContainsKey(itemId);

    /// <summary>
    ///     Get unsafe events for a specific item
    /// </summary>
    public IEnumerable<Event> GetUnsafeEventsForItem(Guid itemId) =>
        _safeBackup.TryGetValue(itemId, out var backup) ? backup.UnsafeEvents : Enumerable.Empty<Event>();

    /// <summary>
    ///     Get all unsafe events across items
    /// </summary>
    public IEnumerable<Event> GetAllUnsafeEvents()
    {
        return _safeBackup.Values.SelectMany(b => b.UnsafeEvents).Distinct().OrderBy(e => e.SortableUniqueIdValue);
    }
}
