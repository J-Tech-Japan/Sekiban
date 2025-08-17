using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Improved projection state manager with unified projection logic
/// </summary>
/// <typeparam name="T">The type of data being projected</typeparam>
public record SafeUnsafeProjectionStateV4<T> where T : class
{
    /// <summary>
    ///     Current data (includes unsafe modifications)
    ///     Optimized for queries
    /// </summary>
    private readonly Dictionary<Guid, T> _currentData = new();

    /// <summary>
    ///     Safe state backup with list of unsafe events that modified it
    ///     Only exists for items with unsafe modifications
    /// </summary>
    private readonly Dictionary<Guid, SafeStateBackup<T>> _safeBackup = new();

    /// <summary>
    ///     SafeWindow threshold
    /// </summary>
    public string SafeWindowThreshold { get; init; } = string.Empty;

    public SafeUnsafeProjectionStateV4() { }

    private SafeUnsafeProjectionStateV4(
        Dictionary<Guid, T> currentData,
        Dictionary<Guid, SafeStateBackup<T>> safeBackup,
        string safeWindowThreshold)
    {
        _currentData = new Dictionary<Guid, T>(currentData);
        _safeBackup = new Dictionary<Guid, SafeStateBackup<T>>(safeBackup);
        SafeWindowThreshold = safeWindowThreshold;
    }

    /// <summary>
    ///     Process an event with a unified projection function
    /// </summary>
    /// <param name="evt">Event to process</param>
    /// <param name="projectionFunction">Function that handles all event types and returns projection requests</param>
    /// <returns>New state after processing</returns>
    public SafeUnsafeProjectionStateV4<T> ProcessEvent(
        Event evt,
        Func<Event, IEnumerable<ProjectionRequest<T>>> projectionFunction)
    {
        // Get projection requests from the unified function
        var requests = projectionFunction(evt).ToList();
        if (requests.Count == 0)
        {
            return this; // No projections requested
        }

        var isEventSafe = string.Compare(evt.SortableUniqueIdValue, SafeWindowThreshold, StringComparison.Ordinal) <= 0;

        // First, process any events that have become safe
        var currentState = ProcessNewlySafeEvents(projectionFunction);

        // Now process the current event
        if (isEventSafe)
        {
            return currentState.ProcessSafeProjection(requests, evt);
        }
        return currentState.ProcessUnsafeProjection(requests, evt);
    }

    /// <summary>
    ///     Process multiple events with a unified projection function
    /// </summary>
    /// <param name="events">Events to process</param>
    /// <param name="projectionFunction">Function that handles all event types and returns projection requests</param>
    /// <returns>New state after processing all events</returns>
    public SafeUnsafeProjectionStateV4<T> ProcessEvents(
        IEnumerable<Event> events,
        Func<Event, IEnumerable<ProjectionRequest<T>>> projectionFunction)
    {
        var state = this;
        foreach (var evt in events)
        {
            state = state.ProcessEvent(evt, projectionFunction);
        }
        return state;
    }

    /// <summary>
    ///     Process events that have transitioned from unsafe to safe
    /// </summary>
    private SafeUnsafeProjectionStateV4<T> ProcessNewlySafeEvents(
        Func<Event, IEnumerable<ProjectionRequest<T>>> projectionFunction)
    {
        var newCurrentData = new Dictionary<Guid, T>(_currentData);
        var newSafeBackup = new Dictionary<Guid, SafeStateBackup<T>>();
        var hasChanges = false;

        // Check each backup to see if any unsafe events have become safe
        foreach (var kvp in _safeBackup)
        {
            var itemId = kvp.Key;
            var backup = kvp.Value;

            // Separate events into safe and still-unsafe
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
                
                // Reprocess from safe state
                var currentItemState = backup.SafeState;
                
                // Apply each event that became safe
                foreach (var safeEvent in nowSafeEvents)
                {
                    var requests = projectionFunction(safeEvent)
                        .Where(r => r.ItemId == itemId)
                        .ToList();
                    
                    foreach (var request in requests)
                    {
                        currentItemState = request.Projector(currentItemState);
                    }
                }

                if (stillUnsafeEvents.Count > 0)
                {
                    // Update backup with new safe state
                    newSafeBackup[itemId] = new SafeStateBackup<T>(currentItemState!, stillUnsafeEvents);
                    
                    // Reprocess unsafe events on top of new safe state
                    var unsafeState = currentItemState;
                    foreach (var unsafeEvent in stillUnsafeEvents)
                    {
                        var requests = projectionFunction(unsafeEvent)
                            .Where(r => r.ItemId == itemId)
                            .ToList();
                        
                        foreach (var request in requests)
                        {
                            unsafeState = request.Projector(unsafeState);
                        }
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
                    // No more unsafe events, update current data directly
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
                // All events still unsafe, keep backup as is
                newSafeBackup[itemId] = backup;
            }
        }

        if (!hasChanges)
        {
            return this;
        }

        return new SafeUnsafeProjectionStateV4<T>(newCurrentData, newSafeBackup, SafeWindowThreshold);
    }

    /// <summary>
    ///     Process safe projection requests
    /// </summary>
    private SafeUnsafeProjectionStateV4<T> ProcessSafeProjection(List<ProjectionRequest<T>> requests, Event evt)
    {
        var newCurrentData = new Dictionary<Guid, T>(_currentData);
        var newSafeBackup = new Dictionary<Guid, SafeStateBackup<T>>(_safeBackup);

        foreach (var request in requests)
        {
            // For safe events, check if we're updating an item with unsafe modifications
            if (_safeBackup.TryGetValue(request.ItemId, out var backup))
            {
                // This item has unsafe modifications
                // Update the safe backup state
                var updatedSafeState = request.Projector(backup.SafeState);

                if (updatedSafeState != null)
                {
                    // Update the backup with new safe state
                    newSafeBackup[request.ItemId] = backup with { SafeState = updatedSafeState };

                    // Current data stays as is (has unsafe modifications applied)
                    // We don't update current because it has unsafe events applied
                }
                else
                {
                    // Item deleted in safe state
                    newSafeBackup.Remove(request.ItemId);
                    // Also remove from current if no unsafe events
                    if (backup.UnsafeEvents.Count == 0)
                    {
                        newCurrentData.Remove(request.ItemId);
                    }
                }
            }
            else
            {
                // No unsafe modifications, directly update current
                var currentValue = newCurrentData.TryGetValue(request.ItemId, out var existing) ? existing : null;
                var newValue = request.Projector(currentValue);

                if (newValue != null)
                {
                    newCurrentData[request.ItemId] = newValue;
                }
                else
                {
                    newCurrentData.Remove(request.ItemId);
                }
            }
        }

        return new SafeUnsafeProjectionStateV4<T>(newCurrentData, newSafeBackup, SafeWindowThreshold);
    }

    /// <summary>
    ///     Process unsafe projection requests
    /// </summary>
    private SafeUnsafeProjectionStateV4<T> ProcessUnsafeProjection(List<ProjectionRequest<T>> requests, Event evt)
    {
        var newCurrentData = new Dictionary<Guid, T>(_currentData);
        var newSafeBackup = new Dictionary<Guid, SafeStateBackup<T>>(_safeBackup);

        foreach (var request in requests)
        {
            if (_safeBackup.TryGetValue(request.ItemId, out var existingBackup))
            {
                // Already has unsafe modifications, add this event to the list
                var updatedEvents = new List<Event>(existingBackup.UnsafeEvents) { evt };
                newSafeBackup[request.ItemId] = existingBackup with { UnsafeEvents = updatedEvents };

                // Apply projection to current data
                var currentValue = newCurrentData.TryGetValue(request.ItemId, out var existing) ? existing : null;
                var newValue = request.Projector(currentValue);

                if (newValue != null)
                {
                    newCurrentData[request.ItemId] = newValue;
                }
                else
                {
                    newCurrentData.Remove(request.ItemId);
                    newSafeBackup.Remove(request.ItemId);
                }
            }
            else
            {
                // First unsafe modification for this item
                // Backup current (safe) state before modifying
                var safeState = newCurrentData.TryGetValue(request.ItemId, out var existing) ? existing : null;

                // Apply projection
                var newValue = request.Projector(safeState);

                if (newValue != null)
                {
                    newCurrentData[request.ItemId] = newValue;

                    // Only create backup if there was a safe state to backup
                    if (safeState != null)
                    {
                        newSafeBackup[request.ItemId] = new SafeStateBackup<T>(safeState, new List<Event> { evt });
                    }
                    else
                    {
                        // New item created by unsafe event, track it
                        // Use null or a default instance as safe state
                        newSafeBackup[request.ItemId] = new SafeStateBackup<T>(
                            null!, // No previous safe state
                            new List<Event> { evt });
                    }
                }
                // If newValue is null and no existing item, nothing to do
            }
        }

        return new SafeUnsafeProjectionStateV4<T>(newCurrentData, newSafeBackup, SafeWindowThreshold);
    }

    /// <summary>
    ///     Update SafeWindow threshold and reprocess events if needed
    /// </summary>
    public SafeUnsafeProjectionStateV4<T> UpdateSafeWindowThreshold(
        string newThreshold,
        Func<Event, IEnumerable<ProjectionRequest<T>>> projectionFunction)
    {
        var newState = this with { SafeWindowThreshold = newThreshold };
        return newState.ProcessNewlySafeEvents(projectionFunction);
    }

    /// <summary>
    ///     Get current state for queries (fast)
    /// </summary>
    public IReadOnlyDictionary<Guid, T> GetCurrentState() => _currentData;

    /// <summary>
    ///     Get safe state only
    /// </summary>
    public IReadOnlyDictionary<Guid, T> GetSafeState()
    {
        var result = new Dictionary<Guid, T>();

        foreach (var kvp in _currentData)
        {
            if (_safeBackup.TryGetValue(kvp.Key, out var backup))
            {
                // Has unsafe modifications, use backed up safe state
                if (backup.SafeState != null)
                {
                    result[kvp.Key] = backup.SafeState;
                }
                // If SafeState is null, item was created by unsafe event, skip it
            }
            else
            {
                // No unsafe modifications, current is safe
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    /// <summary>
    ///     Check if specific item has unsafe modifications
    /// </summary>
    public bool IsItemUnsafe(Guid itemId) => _safeBackup.ContainsKey(itemId);

    /// <summary>
    ///     Get unsafe events for a specific item
    /// </summary>
    public IEnumerable<Event> GetUnsafeEventsForItem(Guid itemId) =>
        _safeBackup.TryGetValue(itemId, out var backup) ? backup.UnsafeEvents : Enumerable.Empty<Event>();

    /// <summary>
    ///     Get all unsafe events across all items
    /// </summary>
    public IEnumerable<Event> GetAllUnsafeEvents()
    {
        return _safeBackup.Values.SelectMany(b => b.UnsafeEvents).Distinct().OrderBy(e => e.SortableUniqueIdValue);
    }
}