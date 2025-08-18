using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Projection state manager with generic key type support
/// </summary>
/// <typeparam name="TKey">The type of the key for items</typeparam>
/// <typeparam name="TState">The type of data being projected</typeparam>
public record SafeUnsafeProjectionStateV7<TKey, TState> 
    where TKey : notnull
    where TState : class
{
    /// <summary>
    ///     Current data (includes unsafe modifications)
    ///     Optimized for queries
    /// </summary>
    private readonly Dictionary<TKey, TState> _currentData = new();

    /// <summary>
    ///     Safe state backup with list of unsafe events that modified it
    ///     Only exists for items with unsafe modifications
    /// </summary>
    private readonly Dictionary<TKey, SafeStateBackup<TState>> _safeBackup = new();

    /// <summary>
    ///     SafeWindow threshold
    /// </summary>
    public string SafeWindowThreshold { get; init; } = string.Empty;

    public SafeUnsafeProjectionStateV7() { }

    private SafeUnsafeProjectionStateV7(
        Dictionary<TKey, TState> currentData,
        Dictionary<TKey, SafeStateBackup<TState>> safeBackup,
        string safeWindowThreshold,
        HashSet<Guid>? processedEventIds = null)
    {
        _currentData = new Dictionary<TKey, TState>(currentData);
        _safeBackup = new Dictionary<TKey, SafeStateBackup<TState>>(safeBackup);
        SafeWindowThreshold = safeWindowThreshold;
        _processedEventIds = processedEventIds ?? new HashSet<Guid>();
    }

    /// <summary>
    ///     Process an event with separated key selection and projection logic
    /// </summary>
    /// <param name="evt">Event to process</param>
    /// <param name="getAffectedItemKeys">Function to determine which items are affected by this event</param>
    /// <param name="projectItem">Function to project a single item given its key, current state, and the event</param>
    /// <returns>New state after processing</returns>
    public SafeUnsafeProjectionStateV7<TKey, TState> ProcessEvent(
        Event evt,
        Func<Event, IEnumerable<TKey>> getAffectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem)
    {
        // Get affected item keys
        var affectedItemKeys = getAffectedItemKeys(evt).ToList();
        if (affectedItemKeys.Count == 0)
        {
            return this; // No items affected
        }

        var isEventSafe = string.Compare(evt.SortableUniqueIdValue, SafeWindowThreshold, StringComparison.Ordinal) <= 0;

        // First, process any events that have become safe
        var currentState = ProcessNewlySafeEvents(getAffectedItemKeys, projectItem);

        // Now process the current event
        if (isEventSafe)
        {
            return currentState.ProcessSafeEvent(evt, affectedItemKeys, projectItem);
        }
        return currentState.ProcessUnsafeEvent(evt, affectedItemKeys, projectItem);
    }

    /// <summary>
    ///     Process multiple events with separated logic
    /// </summary>
    public SafeUnsafeProjectionStateV7<TKey, TState> ProcessEvents(
        IEnumerable<Event> events,
        Func<Event, IEnumerable<TKey>> getAffectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem)
    {
        var state = this;
        foreach (var evt in events)
        {
            state = state.ProcessEvent(evt, getAffectedItemKeys, projectItem);
        }
        return state;
    }

    /// <summary>
    ///     Process events that have transitioned from unsafe to safe
    /// </summary>
    private SafeUnsafeProjectionStateV7<TKey, TState> ProcessNewlySafeEvents(
        Func<Event, IEnumerable<TKey>> getAffectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem)
    {
        var newCurrentData = new Dictionary<TKey, TState>(_currentData);
        var newSafeBackup = new Dictionary<TKey, SafeStateBackup<TState>>();
        var hasChanges = false;

        foreach (var kvp in _safeBackup)
        {
            var itemKey = kvp.Key;
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
                
                // Apply each event that became safe (filtering duplicates)
                var seenEventIds = new HashSet<Guid>();
                foreach (var safeEvent in nowSafeEvents)
                {
                    // Skip duplicate events
                    if (!seenEventIds.Add(safeEvent.Id))
                    {
                        continue;
                    }
                    
                    // Check if this event affects this item
                    var affectedKeys = getAffectedItemKeys(safeEvent);
                    if (affectedKeys.Contains(itemKey))
                    {
                        currentItemState = projectItem(itemKey, currentItemState, safeEvent);
                    }
                }

                if (stillUnsafeEvents.Count > 0)
                {
                    // Update backup with new safe state
                    newSafeBackup[itemKey] = new SafeStateBackup<TState>(currentItemState!, stillUnsafeEvents);
                    
                    // Reprocess unsafe events on top of new safe state
                    var unsafeState = currentItemState;
                    foreach (var unsafeEvent in stillUnsafeEvents)
                    {
                        var affectedKeys = getAffectedItemKeys(unsafeEvent);
                        if (affectedKeys.Contains(itemKey))
                        {
                            unsafeState = projectItem(itemKey, unsafeState, unsafeEvent);
                        }
                    }
                    
                    if (unsafeState != null)
                    {
                        newCurrentData[itemKey] = unsafeState;
                    }
                    else
                    {
                        newCurrentData.Remove(itemKey);
                    }
                }
                else
                {
                    // No more unsafe events, update current data directly
                    if (currentItemState != null)
                    {
                        newCurrentData[itemKey] = currentItemState;
                    }
                    else
                    {
                        newCurrentData.Remove(itemKey);
                    }
                }
            }
            else
            {
                // All events still unsafe, keep backup as is
                newSafeBackup[itemKey] = backup;
            }
        }

        if (!hasChanges)
        {
            return this;
        }

        return new SafeUnsafeProjectionStateV7<TKey, TState>(newCurrentData, newSafeBackup, SafeWindowThreshold, _processedEventIds);
    }

    /// <summary>
    ///     Track processed event IDs to prevent duplicates
    /// </summary>
    private readonly HashSet<Guid> _processedEventIds = new();

    /// <summary>
    ///     Process a safe event
    /// </summary>
    private SafeUnsafeProjectionStateV7<TKey, TState> ProcessSafeEvent(
        Event evt,
        List<TKey> affectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem)
    {
        // Check for duplicate event
        if (_processedEventIds.Contains(evt.Id))
        {
            // Duplicate event - ignore without error
            return this;
        }

        var newCurrentData = new Dictionary<TKey, TState>(_currentData);
        var newSafeBackup = new Dictionary<TKey, SafeStateBackup<TState>>(_safeBackup);
        var newProcessedEventIds = new HashSet<Guid>(_processedEventIds) { evt.Id };

        foreach (var itemKey in affectedItemKeys)
        {
            // For safe events, check if we're updating an item with unsafe modifications
            if (_safeBackup.TryGetValue(itemKey, out var backup))
            {
                // This item has unsafe modifications
                // Update the safe backup state
                var updatedSafeState = projectItem(itemKey, backup.SafeState, evt);

                if (updatedSafeState != null)
                {
                    // Update the backup with new safe state
                    newSafeBackup[itemKey] = backup with { SafeState = updatedSafeState };
                    // Current data stays as is (has unsafe modifications applied)
                }
                else
                {
                    // Item deleted in safe state
                    newSafeBackup.Remove(itemKey);
                    // Also remove from current if no unsafe events
                    if (backup.UnsafeEvents.Count == 0)
                    {
                        newCurrentData.Remove(itemKey);
                    }
                }
            }
            else
            {
                // No unsafe modifications, directly update current
                var currentValue = newCurrentData.TryGetValue(itemKey, out var existing) ? existing : null;
                var newValue = projectItem(itemKey, currentValue, evt);

                if (newValue != null)
                {
                    newCurrentData[itemKey] = newValue;
                }
                else
                {
                    newCurrentData.Remove(itemKey);
                }
            }
        }

        return new SafeUnsafeProjectionStateV7<TKey, TState>(
            newCurrentData, 
            newSafeBackup, 
            SafeWindowThreshold,
            newProcessedEventIds);
    }

    /// <summary>
    ///     Process an unsafe event
    /// </summary>
    private SafeUnsafeProjectionStateV7<TKey, TState> ProcessUnsafeEvent(
        Event evt,
        List<TKey> affectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem)
    {
        // Check for duplicate event at the global level
        if (_processedEventIds.Contains(evt.Id))
        {
            // Duplicate event - ignore without error
            return this;
        }

        var newCurrentData = new Dictionary<TKey, TState>(_currentData);
        var newSafeBackup = new Dictionary<TKey, SafeStateBackup<TState>>(_safeBackup);
        var newProcessedEventIds = new HashSet<Guid>(_processedEventIds) { evt.Id };

        foreach (var itemKey in affectedItemKeys)
        {
            if (_safeBackup.TryGetValue(itemKey, out var existingBackup))
            {
                // Check if this event has already been processed (duplicate check in unsafe events list)
                if (existingBackup.UnsafeEvents.Any(e => e.Id == evt.Id))
                {
                    // Duplicate event - skip processing for this item
                    continue;
                }
                
                // Already has unsafe modifications, add this event to the list
                var updatedEvents = new List<Event>(existingBackup.UnsafeEvents) { evt };
                newSafeBackup[itemKey] = existingBackup with { UnsafeEvents = updatedEvents };

                // Apply projection to current data
                var currentValue = newCurrentData.TryGetValue(itemKey, out var existing) ? existing : null;
                var newValue = projectItem(itemKey, currentValue, evt);

                if (newValue != null)
                {
                    newCurrentData[itemKey] = newValue;
                }
                else
                {
                    newCurrentData.Remove(itemKey);
                    newSafeBackup.Remove(itemKey);
                }
            }
            else
            {
                // First unsafe modification for this item
                // Backup current (safe) state before modifying
                var safeState = newCurrentData.TryGetValue(itemKey, out var existing) ? existing : null;

                // Apply projection
                var newValue = projectItem(itemKey, safeState, evt);

                if (newValue != null)
                {
                    newCurrentData[itemKey] = newValue;

                    // Only create backup if there was a safe state to backup
                    if (safeState != null)
                    {
                        newSafeBackup[itemKey] = new SafeStateBackup<TState>(safeState, new List<Event> { evt });
                    }
                    else
                    {
                        // New item created by unsafe event, track it
                        newSafeBackup[itemKey] = new SafeStateBackup<TState>(
                            null!, // No previous safe state
                            new List<Event> { evt });
                    }
                }
                // If newValue is null and no existing item, nothing to do
            }
        }

        return new SafeUnsafeProjectionStateV7<TKey, TState>(
            newCurrentData, 
            newSafeBackup, 
            SafeWindowThreshold,
            newProcessedEventIds);
    }

    /// <summary>
    ///     Update SafeWindow threshold and reprocess events if needed
    /// </summary>
    public SafeUnsafeProjectionStateV7<TKey, TState> UpdateSafeWindowThreshold(
        string newThreshold,
        Func<Event, IEnumerable<TKey>> getAffectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem)
    {
        var newState = this with { SafeWindowThreshold = newThreshold };
        return newState.ProcessNewlySafeEvents(getAffectedItemKeys, projectItem);
    }

    /// <summary>
    ///     Get current state for queries (fast)
    /// </summary>
    public IReadOnlyDictionary<TKey, TState> GetCurrentState() => _currentData;

    /// <summary>
    ///     Get safe state only
    /// </summary>
    public IReadOnlyDictionary<TKey, TState> GetSafeState()
    {
        var result = new Dictionary<TKey, TState>();

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
    public bool IsItemUnsafe(TKey itemKey) => _safeBackup.ContainsKey(itemKey);

    /// <summary>
    ///     Get unsafe events for a specific item
    /// </summary>
    public IEnumerable<Event> GetUnsafeEventsForItem(TKey itemKey) =>
        _safeBackup.TryGetValue(itemKey, out var backup) ? backup.UnsafeEvents : Enumerable.Empty<Event>();

    /// <summary>
    ///     Get all unsafe events across all items
    /// </summary>
    public IEnumerable<Event> GetAllUnsafeEvents()
    {
        return _safeBackup.Values.SelectMany(b => b.UnsafeEvents).Distinct().OrderBy(e => e.SortableUniqueIdValue);
    }

    /// <summary>
    ///     Get all item keys
    /// </summary>
    public IEnumerable<TKey> GetAllKeys() => _currentData.Keys;

    /// <summary>
    ///     Get all safe item keys
    /// </summary>
    public IEnumerable<TKey> GetSafeKeys() => 
        _currentData.Keys.Where(key => !_safeBackup.ContainsKey(key));

    /// <summary>
    ///     Get all unsafe item keys
    /// </summary>
    public IEnumerable<TKey> GetUnsafeKeys() => _safeBackup.Keys;
}

/// <summary>
///     Type alias for backward compatibility with Guid keys
/// </summary>
public record SafeUnsafeProjectionStateV7<TState> : SafeUnsafeProjectionStateV7<Guid, TState>
    where TState : class
{
    public SafeUnsafeProjectionStateV7() : base()
    {
    }
}

/// <summary>
///     Helper factory methods for creating projection states with different key types
/// </summary>
public static class ProjectionStateV7Factory
{
    /// <summary>
    ///     Create a GUID-keyed projection state (default)
    /// </summary>
    public static SafeUnsafeProjectionStateV7<Guid, T> CreateGuidKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionStateV7<Guid, T>();
    }

    /// <summary>
    ///     Create a string-keyed projection state
    /// </summary>
    public static SafeUnsafeProjectionStateV7<string, T> CreateStringKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionStateV7<string, T>();
    }

    /// <summary>
    ///     Create a composite-keyed projection state
    /// </summary>
    public static SafeUnsafeProjectionStateV7<(string Category, Guid Id), T> CreateCompositeKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionStateV7<(string Category, Guid Id), T>();
    }

    /// <summary>
    ///     Create an int-keyed projection state
    /// </summary>
    public static SafeUnsafeProjectionStateV7<int, T> CreateIntKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionStateV7<int, T>();
    }

    /// <summary>
    ///     Create a long-keyed projection state
    /// </summary>
    public static SafeUnsafeProjectionStateV7<long, T> CreateLongKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionStateV7<long, T>();
    }
}