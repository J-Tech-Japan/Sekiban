using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Projection state manager with generic key type support
/// </summary>
/// <typeparam name="TKey">The type of the key for items</typeparam>
/// <typeparam name="TState">The type of data being projected</typeparam>
public record SafeUnsafeProjectionState<TKey, TState> where TKey : notnull where TState : class
{
    /// <summary>
    ///     Current data (includes unsafe modifications)
    ///     Optimized for queries
    /// </summary>
    private readonly Dictionary<TKey, TState> _currentData = new();

    /// <summary>
    ///     Track processed event IDs to prevent duplicates (only for unsafe events)
    /// </summary>
    private readonly HashSet<Guid> _processedEventIds = new();

    /// <summary>
    ///     Safe state backup with list of unsafe events that modified it
    ///     Only exists for items with unsafe modifications
    /// </summary>
    private readonly Dictionary<TKey, SafeStateBackup<TState>> _safeBackup = new();

    /// <summary>
    ///     Track last processed safe window threshold to avoid redundant processing
    /// </summary>
    private readonly string? _lastProcessedThreshold;

    public SafeUnsafeProjectionState() { }

    private SafeUnsafeProjectionState(
        Dictionary<TKey, TState> currentData,
        Dictionary<TKey, SafeStateBackup<TState>> safeBackup,
        HashSet<Guid>? processedEventIds = null,
        string? lastProcessedThreshold = null)
    {
        _currentData = new Dictionary<TKey, TState>(currentData);
        _safeBackup = new Dictionary<TKey, SafeStateBackup<TState>>(safeBackup);
        _processedEventIds = processedEventIds ?? new HashSet<Guid>();
        _lastProcessedThreshold = lastProcessedThreshold;
    }

    /// <summary>
    ///     Process an event with separated key selection and projection logic
    /// </summary>
    /// <param name="evt">Event to process</param>
    /// <param name="getAffectedItemKeys">Function to determine which items are affected by this event</param>
    /// <param name="projectItem">Function to project a single item given its key, current state, and the event</param>
    /// <returns>New state after processing</returns>
    public SafeUnsafeProjectionState<TKey, TState> ProcessEvent(
        Event evt,
        Func<Event, IEnumerable<TKey>> getAffectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem,
        string? safeWindowThreshold = null)
    {
        var state = this;
        
        // Process newly safe events if threshold has changed
        if (!string.IsNullOrEmpty(safeWindowThreshold) && 
            safeWindowThreshold != _lastProcessedThreshold &&
            _safeBackup.Count > 0)
        {
            state = state.ProcessNewlySafeEvents(safeWindowThreshold, getAffectedItemKeys, projectItem);
        }
        
        // Get affected item keys
        var affectedItemKeys = getAffectedItemKeys(evt).ToList();
        if (affectedItemKeys.Count == 0)
        {
            return state; // No items affected
        }

        // If no threshold provided, treat all events as safe
        var isEventSafe = string.IsNullOrEmpty(safeWindowThreshold) || 
                          string.Compare(evt.SortableUniqueIdValue, safeWindowThreshold, StringComparison.Ordinal) <= 0;

        // Now process the current event
        if (isEventSafe)
        {
            return state.ProcessSafeEvent(evt, affectedItemKeys, projectItem, safeWindowThreshold);
        }
        return state.ProcessUnsafeEvent(evt, affectedItemKeys, projectItem, safeWindowThreshold);
    }

    /// <summary>
    ///     Process multiple events with separated logic
    /// </summary>
    public SafeUnsafeProjectionState<TKey, TState> ProcessEvents(
        IEnumerable<Event> events,
        Func<Event, IEnumerable<TKey>> getAffectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem,
        string? safeWindowThreshold = null)
    {
        var state = this;
        foreach (var evt in events)
        {
            state = state.ProcessEvent(evt, getAffectedItemKeys, projectItem, safeWindowThreshold);
        }
        return state;
    }

    /// <summary>
    ///     Process events that have transitioned from unsafe to safe
    /// </summary>
    private SafeUnsafeProjectionState<TKey, TState> ProcessNewlySafeEvents(
        string safeWindowThreshold,
        Func<Event, IEnumerable<TKey>> getAffectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem)
    {
        var newCurrentData = new Dictionary<TKey, TState>(_currentData);
        var newSafeBackup = new Dictionary<TKey, SafeStateBackup<TState>>();
        var newProcessedEventIds = new HashSet<Guid>(_processedEventIds);
        var hasChanges = false;

        foreach (var kvp in _safeBackup)
        {
            var itemKey = kvp.Key;
            var backup = kvp.Value;

            // Separate events into safe and still-unsafe
            var nowSafeEvents = backup
                .UnsafeEvents
                .Where(e => string.Compare(e.SortableUniqueIdValue, safeWindowThreshold, StringComparison.Ordinal) <= 0)
                .OrderBy(e => e.SortableUniqueIdValue)
                .ToList();

            var stillUnsafeEvents = backup
                .UnsafeEvents
                .Where(e => string.Compare(e.SortableUniqueIdValue, safeWindowThreshold, StringComparison.Ordinal) > 0)
                .ToList();

            if (nowSafeEvents.Count > 0)
            {
                hasChanges = true;

                // Remove event IDs that are now safe from the processed set
                foreach (var safeEvent in nowSafeEvents)
                {
                    newProcessedEventIds.Remove(safeEvent.Id);
                }

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
                    // If item was deleted in safe state, we still need to track the unsafe events
                    if (currentItemState != null)
                    {
                        // Update backup with new safe state
                        newSafeBackup[itemKey] = new SafeStateBackup<TState>(currentItemState, stillUnsafeEvents);
                    } else
                    {
                        // Item was deleted in safe state but has unsafe events
                        // Don't keep the backup if safe state is deleted
                        // The unsafe events might re-create the item
                    }

                    // Reprocess unsafe events on top of new safe state (which might be null)
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
                        // If safe state was null (deleted) but unsafe events created it, track it
                        if (currentItemState == null)
                        {
                            newSafeBackup[itemKey] = new SafeStateBackup<TState>(null!, stillUnsafeEvents);
                        }
                    } else
                    {
                        newCurrentData.Remove(itemKey);
                        // Item is deleted in both safe and unsafe states, remove backup
                        newSafeBackup.Remove(itemKey);
                    }
                } else
                {
                    // No more unsafe events, item is now fully safe
                    // Keep the item in currentData as-is (it already has the correct state)
                    // Just remove the backup since it's no longer needed
                    // Note: We don't modify newCurrentData here because it already has the correct state
                }
            } else
            {
                // All events still unsafe, keep backup as is
                newSafeBackup[itemKey] = backup;
            }
        }

        if (!hasChanges)
        {
            return new SafeUnsafeProjectionState<TKey, TState>(
                _currentData,
                _safeBackup,
                _processedEventIds,
                safeWindowThreshold);
        }

        return new SafeUnsafeProjectionState<TKey, TState>(
            newCurrentData,
            newSafeBackup,
            newProcessedEventIds,
            safeWindowThreshold);
    }

    /// <summary>
    ///     Process a safe event
    /// </summary>
    private SafeUnsafeProjectionState<TKey, TState> ProcessSafeEvent(
        Event evt,
        List<TKey> affectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem,
        string? safeWindowThreshold = null)
    {
        // Check for duplicate event
        if (_processedEventIds.Contains(evt.Id))
        {
            // Duplicate event - ignore without error
            return this;
        }

        var newCurrentData = new Dictionary<TKey, TState>(_currentData);
        var newSafeBackup = new Dictionary<TKey, SafeStateBackup<TState>>(_safeBackup);
        // Safe events don't need to be tracked in processedEventIds
        var newProcessedEventIds = new HashSet<Guid>(_processedEventIds);

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
                } else
                {
                    // Item deleted in safe state
                    newSafeBackup.Remove(itemKey);
                    // Also remove from current if no unsafe events
                    if (backup.UnsafeEvents.Count == 0)
                    {
                        newCurrentData.Remove(itemKey);
                    }
                }
            } else
            {
                // No unsafe modifications, directly update current
                var currentValue = newCurrentData.TryGetValue(itemKey, out var existing) ? existing : null;
                var newValue = projectItem(itemKey, currentValue, evt);

                if (newValue != null)
                {
                    newCurrentData[itemKey] = newValue;
                } else
                {
                    newCurrentData.Remove(itemKey);
                }
            }
        }

        return new SafeUnsafeProjectionState<TKey, TState>(
            newCurrentData,
            newSafeBackup,
            newProcessedEventIds,
            safeWindowThreshold ?? _lastProcessedThreshold);
    }

    /// <summary>
    ///     Process an unsafe event
    /// </summary>
    private SafeUnsafeProjectionState<TKey, TState> ProcessUnsafeEvent(
        Event evt,
        List<TKey> affectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem,
        string? safeWindowThreshold = null)
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
                } else
                {
                    // Item deleted by unsafe event - remove from current data but keep the backup
                    newCurrentData.Remove(itemKey);
                    // Keep the backup with the unsafe delete event added
                    var updatedUnsafeEvents = new List<Event>(existingBackup.UnsafeEvents) { evt };
                    newSafeBackup[itemKey] = existingBackup with { UnsafeEvents = updatedUnsafeEvents };
                }
            } else
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
                    } else
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

        return new SafeUnsafeProjectionState<TKey, TState>(
            newCurrentData,
            newSafeBackup,
            newProcessedEventIds,
            safeWindowThreshold ?? _lastProcessedThreshold);
    }


    /// <summary>
    ///     Get current state for queries (fast)
    /// </summary>
    public IReadOnlyDictionary<TKey, TState> GetCurrentState() => _currentData;

    /// <summary>
    ///     Get safe state only
    /// </summary>
    public IReadOnlyDictionary<TKey, TState> GetSafeState(
        string safeWindowThreshold,
        Func<Event, IEnumerable<TKey>> getAffectedItemKeys,
        Func<TKey, TState?, Event, TState?> projectItem)
    {
        // If threshold is same as last processed, just return built state
        if (!string.IsNullOrEmpty(safeWindowThreshold) && 
            safeWindowThreshold == _lastProcessedThreshold)
        {
            return BuildSafeStateDictionary();
        }
        
        // Otherwise, we need to calculate safe state dynamically
        // This happens when GetSafeState is called without ProcessEvent being called first
        var result = new Dictionary<TKey, TState>();
        
        // Check each backed up item to see if any events have become safe
        foreach (var kvp in _safeBackup)
        {
            var itemKey = kvp.Key;
            var backup = kvp.Value;
            
            // Separate events into safe and still-unsafe
            var nowSafeEvents = backup.UnsafeEvents
                .Where(e => string.IsNullOrEmpty(safeWindowThreshold) || 
                           string.Compare(e.SortableUniqueIdValue, safeWindowThreshold, StringComparison.Ordinal) <= 0)
                .OrderBy(e => e.SortableUniqueIdValue)
                .ToList();
                
            if (nowSafeEvents.Count > 0)
            {
                // Some events have become safe, recompute the safe state
                var safeState = backup.SafeState;
                
                foreach (var safeEvent in nowSafeEvents)
                {
                    var affectedKeys = getAffectedItemKeys(safeEvent);
                    if (affectedKeys.Contains(itemKey))
                    {
                        safeState = projectItem(itemKey, safeState, safeEvent);
                    }
                }
                
                if (safeState != null)
                {
                    result[itemKey] = safeState;
                }
            }
            else
            {
                // No events have become safe, use the original safe state
                if (backup.SafeState != null)
                {
                    result[itemKey] = backup.SafeState;
                }
            }
        }
        
        // Add items that have no unsafe modifications
        foreach (var kvp in _currentData)
        {
            if (!_safeBackup.ContainsKey(kvp.Key))
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        
        return result;
    }
    
    /// <summary>
    ///     Build safe state dictionary from current internal state
    /// </summary>
    private IReadOnlyDictionary<TKey, TState> BuildSafeStateDictionary()
    {
        var result = new Dictionary<TKey, TState>();
        
        // Add safe backups (items with unsafe modifications use their safe backup)
        foreach (var kvp in _safeBackup)
        {
            if (kvp.Value.SafeState != null)
            {
                result[kvp.Key] = kvp.Value.SafeState;
            }
        }
        
        // Add items that have no unsafe modifications
        foreach (var kvp in _currentData)
        {
            if (!_safeBackup.ContainsKey(kvp.Key))
            {
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
public record SafeUnsafeProjectionState<TState> : SafeUnsafeProjectionState<Guid, TState> where TState : class
{
}
