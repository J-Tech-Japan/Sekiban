using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Simplified projection state manager without ProjectionRequest
/// </summary>
/// <typeparam name="T">The type of data being projected</typeparam>
public record SafeUnsafeProjectionStateV6<T> where T : class
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

    public SafeUnsafeProjectionStateV6() { }

    private SafeUnsafeProjectionStateV6(
        Dictionary<Guid, T> currentData,
        Dictionary<Guid, SafeStateBackup<T>> safeBackup,
        string safeWindowThreshold)
    {
        _currentData = new Dictionary<Guid, T>(currentData);
        _safeBackup = new Dictionary<Guid, SafeStateBackup<T>>(safeBackup);
        SafeWindowThreshold = safeWindowThreshold;
    }

    /// <summary>
    ///     Process an event with separated ID selection and projection logic
    /// </summary>
    /// <param name="evt">Event to process</param>
    /// <param name="getAffectedItemIds">Function to determine which items are affected by this event</param>
    /// <param name="projectItem">Function to project a single item given its ID, current state, and the event</param>
    /// <returns>New state after processing</returns>
    public SafeUnsafeProjectionStateV6<T> ProcessEvent(
        Event evt,
        Func<Event, IEnumerable<Guid>> getAffectedItemIds,
        Func<Guid, T?, Event, T?> projectItem)
    {
        // Get affected item IDs
        var affectedItemIds = getAffectedItemIds(evt).ToList();
        if (affectedItemIds.Count == 0)
        {
            return this; // No items affected
        }

        var isEventSafe = string.Compare(evt.SortableUniqueIdValue, SafeWindowThreshold, StringComparison.Ordinal) <= 0;

        // First, process any events that have become safe
        var currentState = ProcessNewlySafeEvents(getAffectedItemIds, projectItem);

        // Now process the current event
        if (isEventSafe)
        {
            return currentState.ProcessSafeEvent(evt, affectedItemIds, projectItem);
        }
        return currentState.ProcessUnsafeEvent(evt, affectedItemIds, projectItem);
    }

    /// <summary>
    ///     Process multiple events with separated logic
    /// </summary>
    public SafeUnsafeProjectionStateV6<T> ProcessEvents(
        IEnumerable<Event> events,
        Func<Event, IEnumerable<Guid>> getAffectedItemIds,
        Func<Guid, T?, Event, T?> projectItem)
    {
        var state = this;
        foreach (var evt in events)
        {
            state = state.ProcessEvent(evt, getAffectedItemIds, projectItem);
        }
        return state;
    }

    /// <summary>
    ///     Process events that have transitioned from unsafe to safe
    /// </summary>
    private SafeUnsafeProjectionStateV6<T> ProcessNewlySafeEvents(
        Func<Event, IEnumerable<Guid>> getAffectedItemIds,
        Func<Guid, T?, Event, T?> projectItem)
    {
        var newCurrentData = new Dictionary<Guid, T>(_currentData);
        var newSafeBackup = new Dictionary<Guid, SafeStateBackup<T>>();
        var hasChanges = false;

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
                    // Check if this event affects this item
                    var affectedIds = getAffectedItemIds(safeEvent);
                    if (affectedIds.Contains(itemId))
                    {
                        currentItemState = projectItem(itemId, currentItemState, safeEvent);
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
                        var affectedIds = getAffectedItemIds(unsafeEvent);
                        if (affectedIds.Contains(itemId))
                        {
                            unsafeState = projectItem(itemId, unsafeState, unsafeEvent);
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

        return new SafeUnsafeProjectionStateV6<T>(newCurrentData, newSafeBackup, SafeWindowThreshold);
    }

    /// <summary>
    ///     Process a safe event
    /// </summary>
    private SafeUnsafeProjectionStateV6<T> ProcessSafeEvent(
        Event evt,
        List<Guid> affectedItemIds,
        Func<Guid, T?, Event, T?> projectItem)
    {
        var newCurrentData = new Dictionary<Guid, T>(_currentData);
        var newSafeBackup = new Dictionary<Guid, SafeStateBackup<T>>(_safeBackup);

        foreach (var itemId in affectedItemIds)
        {
            // For safe events, check if we're updating an item with unsafe modifications
            if (_safeBackup.TryGetValue(itemId, out var backup))
            {
                // This item has unsafe modifications
                // Update the safe backup state
                var updatedSafeState = projectItem(itemId, backup.SafeState, evt);

                if (updatedSafeState != null)
                {
                    // Update the backup with new safe state
                    newSafeBackup[itemId] = backup with { SafeState = updatedSafeState };
                    // Current data stays as is (has unsafe modifications applied)
                }
                else
                {
                    // Item deleted in safe state
                    newSafeBackup.Remove(itemId);
                    // Also remove from current if no unsafe events
                    if (backup.UnsafeEvents.Count == 0)
                    {
                        newCurrentData.Remove(itemId);
                    }
                }
            }
            else
            {
                // No unsafe modifications, directly update current
                var currentValue = newCurrentData.TryGetValue(itemId, out var existing) ? existing : null;
                var newValue = projectItem(itemId, currentValue, evt);

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

        return new SafeUnsafeProjectionStateV6<T>(newCurrentData, newSafeBackup, SafeWindowThreshold);
    }

    /// <summary>
    ///     Process an unsafe event
    /// </summary>
    private SafeUnsafeProjectionStateV6<T> ProcessUnsafeEvent(
        Event evt,
        List<Guid> affectedItemIds,
        Func<Guid, T?, Event, T?> projectItem)
    {
        var newCurrentData = new Dictionary<Guid, T>(_currentData);
        var newSafeBackup = new Dictionary<Guid, SafeStateBackup<T>>(_safeBackup);

        foreach (var itemId in affectedItemIds)
        {
            if (_safeBackup.TryGetValue(itemId, out var existingBackup))
            {
                // Already has unsafe modifications, add this event to the list
                var updatedEvents = new List<Event>(existingBackup.UnsafeEvents) { evt };
                newSafeBackup[itemId] = existingBackup with { UnsafeEvents = updatedEvents };

                // Apply projection to current data
                var currentValue = newCurrentData.TryGetValue(itemId, out var existing) ? existing : null;
                var newValue = projectItem(itemId, currentValue, evt);

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
                // First unsafe modification for this item
                // Backup current (safe) state before modifying
                var safeState = newCurrentData.TryGetValue(itemId, out var existing) ? existing : null;

                // Apply projection
                var newValue = projectItem(itemId, safeState, evt);

                if (newValue != null)
                {
                    newCurrentData[itemId] = newValue;

                    // Only create backup if there was a safe state to backup
                    if (safeState != null)
                    {
                        newSafeBackup[itemId] = new SafeStateBackup<T>(safeState, new List<Event> { evt });
                    }
                    else
                    {
                        // New item created by unsafe event, track it
                        newSafeBackup[itemId] = new SafeStateBackup<T>(
                            null!, // No previous safe state
                            new List<Event> { evt });
                    }
                }
                // If newValue is null and no existing item, nothing to do
            }
        }

        return new SafeUnsafeProjectionStateV6<T>(newCurrentData, newSafeBackup, SafeWindowThreshold);
    }

    /// <summary>
    ///     Update SafeWindow threshold and reprocess events if needed
    /// </summary>
    public SafeUnsafeProjectionStateV6<T> UpdateSafeWindowThreshold(
        string newThreshold,
        Func<Event, IEnumerable<Guid>> getAffectedItemIds,
        Func<Guid, T?, Event, T?> projectItem)
    {
        var newState = this with { SafeWindowThreshold = newThreshold };
        return newState.ProcessNewlySafeEvents(getAffectedItemIds, projectItem);
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

/// <summary>
///     Helper methods for creating projection contexts
/// </summary>
public static class ProjectionContextHelpers
{
    /// <summary>
    ///     Create a projection context from tag-based logic
    /// </summary>
    public static (Func<Event, IEnumerable<Guid>> GetIds, Func<Guid, T?, Event, T?> Project) 
        CreateTagBasedProjection<T>(
            Func<Event, IEnumerable<(Guid Id, object? Context)>> getTaggedItems,
            Func<Guid, T?, Event, object?, T?> projectWithContext) where T : class
    {
        // Create a cache to store context per event
        var contextCache = new Dictionary<(Event, Guid), object?>();
        
        Func<Event, IEnumerable<Guid>> getIds = (evt) =>
        {
            var items = getTaggedItems(evt).ToList();
            // Cache the context for each item
            foreach (var (id, context) in items)
            {
                contextCache[(evt, id)] = context;
            }
            return items.Select(x => x.Id);
        };
        
        Func<Guid, T?, Event, T?> project = (id, state, evt) =>
        {
            // Retrieve context from cache
            contextCache.TryGetValue((evt, id), out var context);
            return projectWithContext(id, state, evt, context);
        };
        
        return (getIds, project);
    }
}