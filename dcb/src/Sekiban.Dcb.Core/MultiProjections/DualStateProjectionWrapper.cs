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
public class DualStateProjectionWrapper<T> : ISafeAndUnsafeStateAccessor<T>, IMultiProjectionPayload, IDualStateAccessor
    where T : IMultiProjectionPayload
{
    // Keep track of safe events until the next persisted safe snapshot boundary.
    private readonly Dictionary<Guid, Event> _allSafeEvents = new();

    // Keep duplicate detection across the current in-memory history until the next compaction boundary.
    private readonly HashSet<Guid> _processedEventIds = new();

    // Buffer for events within SafeWindow (using Dictionary to handle duplicates)
    private readonly Dictionary<Guid, Event> _bufferedEvents = new();

    private readonly string _projectorName;
    private readonly ICoreMultiProjectorTypes _types;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _useIncrementalSafePromotion;

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

    public int SafeVersion => _safeVersion;

    public DualStateProjectionWrapper(
        T initialProjector,
        string projectorName,
        ICoreMultiProjectorTypes types,
        JsonSerializerOptions jsonOptions,
        int initialVersion = 0,
        Guid initialLastEventId = default,
        string? initialLastSortableUniqueId = null)
    {
        _jsonOptions = jsonOptions;
        _safeProjector = initialProjector;
        _projectorName = projectorName;
        _types = types;
        _unsafeProjector = CloneProjector(initialProjector, jsonOptions);
        _useIncrementalSafePromotion = false;

        // Initialize version tracking
        _safeVersion = initialVersion;
        _unsafeVersion = initialVersion;
        _safeLastEventId = initialLastEventId;
        _unsafeLastEventId = initialLastEventId;
        _safeLastSortableUniqueId = initialLastSortableUniqueId ?? string.Empty;
        _unsafeLastSortableUniqueId = initialLastSortableUniqueId ?? string.Empty;
    }

    internal DualStateProjectionWrapper(
        T safeProjector,
        T unsafeProjector,
        string projectorName,
        ICoreMultiProjectorTypes types,
        JsonSerializerOptions jsonOptions,
        int initialVersion,
        Guid initialLastEventId,
        string? initialLastSortableUniqueId)
    {
        _jsonOptions = jsonOptions;
        _safeProjector = safeProjector;
        _unsafeProjector = unsafeProjector;
        _projectorName = projectorName;
        _types = types;
        _useIncrementalSafePromotion = true;

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

        // Debug logging removed to avoid noisy console output.

        if (_processedEventIds.Contains(evt.Id))
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
        _processedEventIds.Add(evt.Id);

        // Store event based on safe window
        if (isInSafeWindow)
        {
            // Buffer event for later processing
            _bufferedEvents[evt.Id] = evt;
        }
        else
        {
            _allSafeEvents[evt.Id] = evt;

            // Process the new safe event directly on the current safe projector.
            var safeProjected = _types.Project(
                _projectorName,
                _safeProjector,
                evt,
                tags,
                domainTypes,
                new SortableUniqueId("000000000000000000000000000000000000000000000000"));

            if (safeProjected.IsSuccess)
            {
                _safeProjector = (T)safeProjected.GetValue();
                _safeLastEventId = evt.Id;
                _safeLastSortableUniqueId = evt.SortableUniqueIdValue;
                _safeVersion++;
            }
        }

        return this;
    }

    // IDualStateAccessor explicit implementation — non-generic access without reflection
    int IDualStateAccessor.UnsafeVersion => _unsafeVersion;
    Guid IDualStateAccessor.UnsafeLastEventId => _unsafeLastEventId;
    string IDualStateAccessor.UnsafeLastSortableUniqueId => _unsafeLastSortableUniqueId;
    string? IDualStateAccessor.SafeLastSortableUniqueId =>
        string.IsNullOrEmpty(_safeLastSortableUniqueId) ? null : _safeLastSortableUniqueId;
    object IDualStateAccessor.GetSafeProjectorPayload() => _safeProjector!;
    object IDualStateAccessor.GetUnsafeProjectorPayload() => _unsafeProjector!;
    IDualStateAccessor IDualStateAccessor.ProcessEventAs(
        Event evt, SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        ProcessEvent(evt, safeWindowThreshold, domainTypes);
        return this;
    }

    void IDualStateAccessor.PromoteBufferedEvents(
        SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        ProcessBufferedEvents(safeWindowThreshold, domainTypes);
    }

    void IDualStateAccessor.CompactSafeHistory()
    {
        var hadSafeEvents = _allSafeEvents.Count > 0;
        _allSafeEvents.Clear();
        if (hadSafeEvents)
        {
            _allSafeEvents.TrimExcess();
        }
        RebuildProcessedEventIdsFromBufferedEvents();
        _useIncrementalSafePromotion = true;
    }

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

            if (_useIncrementalSafePromotion)
            {
                ApplyEventsIncrementally(eventsToProcess, domainTypes);
            }
            else
            {
                RebuildSafeState(domainTypes);
            }

            _safeVersion += eventsToProcess.Count;
        }
    }

    private void RebuildProcessedEventIdsFromBufferedEvents()
    {
        _processedEventIds.Clear();

        foreach (var bufferedEventId in _bufferedEvents.Keys)
        {
            _processedEventIds.Add(bufferedEventId);
        }

        if (_processedEventIds.Count > 0)
        {
            _processedEventIds.TrimExcess();
        }
    }

    private void RebuildSafeState(DcbDomainTypes domainTypes)
    {
        if (_allSafeEvents.Count == 0)
        {
            return;
        }

        var allEvents = _allSafeEvents.Values.ToList();
        allEvents.Sort((a, b) => string.Compare(
            a.SortableUniqueIdValue,
            b.SortableUniqueIdValue,
            StringComparison.Ordinal));

        var rebuiltProjector = _types.GenerateInitialPayload(_projectorName);
        if (!rebuiltProjector.IsSuccess)
        {
            throw rebuiltProjector.GetException();
        }

        var newSafeProjector = (T)rebuiltProjector.GetValue();
        var newSafeLastEventId = Guid.Empty;
        var newSafeLastSortableId = string.Empty;

        foreach (var ev in allEvents)
        {
            var tags = ev.Tags.Select(tagString => domainTypes.TagTypes.GetTag(tagString)).ToList();
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
        }

        _safeProjector = newSafeProjector;
        _safeLastEventId = newSafeLastEventId;
        _safeLastSortableUniqueId = newSafeLastSortableId;
    }

    private void ApplyEventsIncrementally(List<Event> events, DcbDomainTypes domainTypes)
    {
        events.Sort((a, b) => string.Compare(
            a.SortableUniqueIdValue,
            b.SortableUniqueIdValue,
            StringComparison.Ordinal));

        foreach (var ev in events)
        {
            var tags = ev.Tags.Select(tagString => domainTypes.TagTypes.GetTag(tagString)).ToList();
            var projected = _types.Project(
                _projectorName,
                _safeProjector,
                ev,
                tags,
                domainTypes,
                new SortableUniqueId("000000000000000000000000000000000000000000000000"));

            if (!projected.IsSuccess)
            {
                throw projected.GetException();
            }

            _safeProjector = (T)projected.GetValue();
            _safeLastEventId = ev.Id;
            _safeLastSortableUniqueId = ev.SortableUniqueIdValue;
        }
    }

    private static T CloneProjector(T source, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(source, source.GetType(), options);
        var cloned = JsonSerializer.Deserialize(json, source.GetType(), options);
        return (T)cloned!;
    }
}
