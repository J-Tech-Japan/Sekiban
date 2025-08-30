using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using System.Text.Json;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Wrapper class that adapts traditional IMultiProjectionPayload implementations
///     to work with the ISafeAndUnsafeStateAccessor interface by managing safe and unsafe states internally.
/// </summary>
public class DualStateProjectionWrapper<T> : ISafeAndUnsafeStateAccessor<T>, IMultiProjectionPayload
    where T : IMultiProjectionPayload
{
    // Keep track of all safe events for proper rebuilding
    private readonly Dictionary<Guid, Event> _allSafeEvents = new();
    
    // Buffer for events within SafeWindow (using Dictionary to handle duplicates)
    private readonly Dictionary<Guid, Event> _bufferedEvents = new();
    
    private readonly string _projectorName;
    private readonly IMultiProjectorTypes _types;
    private readonly JsonSerializerOptions _jsonOptions;
    
    // Safe state - events older than SafeWindow
    private T _safeProjector;
    private int _safeVersion;
    private Guid _safeLastEventId;
    private string _safeLastSortableUniqueId = string.Empty;
    
    // Unsafe state - includes all events
    private T _unsafeProjector;
    private int _unsafeVersion;
    private Guid _unsafeLastEventId;
    private string _unsafeLastSortableUniqueId = string.Empty;

    public DualStateProjectionWrapper(
        T initialProjector,
        string projectorName,
        IMultiProjectorTypes types,
        JsonSerializerOptions jsonOptions,
        int initialVersion = 0,
        Guid initialLastEventId = default,
        string? initialLastSortableUniqueId = null)
    {
        _safeProjector = initialProjector;
        _unsafeProjector = CloneProjector(initialProjector);
        _projectorName = projectorName;
        _types = types;
        _jsonOptions = jsonOptions;
        
        // Initialize version tracking
        _safeVersion = initialVersion;
        _unsafeVersion = initialVersion;
        _safeLastEventId = initialLastEventId;
        _unsafeLastEventId = initialLastEventId;
        _safeLastSortableUniqueId = initialLastSortableUniqueId ?? string.Empty;
        _unsafeLastSortableUniqueId = initialLastSortableUniqueId ?? string.Empty;
    }

    public SafeProjection<T> GetSafeProjection(SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        ProcessBufferedEvents(safeWindowThreshold, domainTypes);
        return new SafeProjection<T>(_safeProjector, _safeLastSortableUniqueId, _safeVersion);
    }

    public UnsafeProjection<T> GetUnsafeProjection(DcbDomainTypes domainTypes)
        => new UnsafeProjection<T>(_unsafeProjector, _unsafeLastSortableUniqueId, _unsafeLastEventId, _unsafeVersion);
    
    public ISafeAndUnsafeStateAccessor<T> ProcessEvent(
        Event evt,
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes)
    {
        // Check if this event has already been processed (duplicate prevention)
        var eventTime = new SortableUniqueId(evt.SortableUniqueIdValue);
        var isInSafeWindow = !eventTime.IsEarlierThanOrEqual(safeWindowThreshold);
        
        // Check if event already exists in either safe events or buffered events
        if (_allSafeEvents.ContainsKey(evt.Id) || _bufferedEvents.ContainsKey(evt.Id))
        {
            // Event already processed, skip it
            return this;
        }
        
        // Process the event through projection
        var tags = evt.Tags.Select(tagString => domainTypes.TagTypes.GetTag(tagString)).ToList();
        var unsafeProjected = _types.Project(
            _projectorName,
            _unsafeProjector,
            evt,
            tags,
            domainTypes,
            safeWindowThreshold);
        
        if (!unsafeProjected.IsSuccess)
        {
            throw unsafeProjected.GetException();
        }
        
        _unsafeProjector = (T)unsafeProjected.GetValue();
        _unsafeLastEventId = evt.Id;
        _unsafeLastSortableUniqueId = evt.SortableUniqueIdValue;
        _unsafeVersion++;
        
        // Store event based on safe window
        if (isInSafeWindow)
        {
            // Buffer event for later processing
            _bufferedEvents[evt.Id] = evt;
        }
        else
        {
            // Add to safe events and process
            _allSafeEvents[evt.Id] = evt;
            RebuildSafeState(domainTypes);
        }
        
        return this;
    }

    // Legacy getters no longer required by interface; kept if external callers rely
    public Guid GetLastEventId() => _unsafeLastEventId;
    public string GetLastSortableUniqueId() => _unsafeLastSortableUniqueId;
    public int GetVersion() => _unsafeVersion;

    private void ProcessBufferedEvents(SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        var eventsToProcess = new List<Event>();
        var keysToRemove = new List<Guid>();
        
        // Find events that are now outside safe window
        foreach (var kvp in _bufferedEvents)
        {
            var ev = kvp.Value;
            var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);
            
            if (eventTime.IsEarlierThanOrEqual(safeWindowThreshold))
            {
                eventsToProcess.Add(ev);
                keysToRemove.Add(kvp.Key);
            }
        }
        
        // Remove processed events from buffer
        foreach (var key in keysToRemove)
        {
            _bufferedEvents.Remove(key);
        }
        
        // Add newly safe events to our collection and rebuild
        if (eventsToProcess.Count > 0)
        {
            foreach (var ev in eventsToProcess)
            {
                _allSafeEvents[ev.Id] = ev;
            }
            
            RebuildSafeState(domainTypes);
        }
    }

    private void RebuildSafeState(DcbDomainTypes domainTypes)
    {
        // Get all safe events and sort them by SortableUniqueId
        var allEvents = _allSafeEvents.Values.ToList();
        allEvents.Sort((a, b) => string.Compare(
            a.SortableUniqueIdValue,
            b.SortableUniqueIdValue,
            StringComparison.Ordinal));
        
        // Rebuild safe state from scratch
        var rebuiltProjector = _types.GenerateInitialPayload(_projectorName);
        if (!rebuiltProjector.IsSuccess)
        {
            throw rebuiltProjector.GetException();
        }
        
        var newSafeProjector = (T)rebuiltProjector.GetValue();
        var newSafeVersion = 0;
        var newSafeLastEventId = Guid.Empty;
        var newSafeLastSortableId = string.Empty;
        
        foreach (var ev in allEvents)
        {
            var tags = ev.Tags.Select(tagString => domainTypes.TagTypes.GetTag(tagString)).ToList();
            // Safe rebuild uses minimum threshold since all events are already safe
            var projected = _types.Project(
                _projectorName,
                newSafeProjector,
                ev,
                tags,
                domainTypes,
                new SortableUniqueId("000000000000000000000000000000000000000000000000"));
            
            if (!projected.IsSuccess)
            {
                throw projected.GetException();
            }
            
            newSafeProjector = (T)projected.GetValue();
            newSafeLastEventId = ev.Id;
            newSafeLastSortableId = ev.SortableUniqueIdValue;
            newSafeVersion++;
        }
        
        _safeProjector = newSafeProjector;
        _safeLastEventId = newSafeLastEventId;
        _safeLastSortableUniqueId = newSafeLastSortableId;
        _safeVersion = newSafeVersion;
    }

    private T CloneProjector(T source)
    {
        // Serialize and deserialize to create a deep clone
        var json = JsonSerializer.Serialize(source, source.GetType(), _jsonOptions);
        var cloned = JsonSerializer.Deserialize(json, source.GetType(), _jsonOptions);
        return (T)cloned!;
    }
}
