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
    // Keep track of all safe events for proper rebuilding
    private readonly Dictionary<Guid, Event> _allSafeEvents = new();

    // Buffer for events within SafeWindow (using Dictionary to handle duplicates)
    private readonly Dictionary<Guid, Event> _bufferedEvents = new();

    private readonly string _projectorName;
    private readonly ICoreMultiProjectorTypes _types;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly bool _isRestoredFromSnapshot;

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
        : this(
            initialProjector,
            projectorName,
            types,
            jsonOptions,
            initialVersion,
            initialLastEventId,
            initialLastSortableUniqueId,
            false)
    {
    }

    public DualStateProjectionWrapper(
        T initialProjector,
        string projectorName,
        ICoreMultiProjectorTypes types,
        JsonSerializerOptions jsonOptions,
        int initialVersion,
        Guid initialLastEventId,
        string? initialLastSortableUniqueId,
        bool isRestoredFromSnapshot)
    {
        _safeProjector = initialProjector;
        _unsafeProjector = CloneProjector(initialProjector);
        _projectorName = projectorName;
        _types = types;
        _jsonOptions = jsonOptions;
        _isRestoredFromSnapshot = isRestoredFromSnapshot;

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

        // Debug logging removed to avoid noisy console output.

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
            // Add to safe events collection
            _allSafeEvents[evt.Id] = evt;

            // Incremental update of safe state instead of full rebuild
            // Process just this new event through the safe projector
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
            // Increment safe version for each promoted event
            var promotedCount = eventsToProcess.Count;

            foreach (var ev in eventsToProcess)
            {
                _allSafeEvents[ev.Id] = ev;
            }

            if (_isRestoredFromSnapshot)
            {
                ApplyEventsIncrementally(eventsToProcess, domainTypes);
            }
            else
            {
                RebuildSafeState(domainTypes);
            }

            // After rebuild, increment the safe version by the number of promoted events
            _safeVersion += promotedCount;
        }
    }

    private void RebuildSafeState(DcbDomainTypes domainTypes)
    {
        // Guard: Skip rebuild if _allSafeEvents is empty.
        // This is critical after snapshot restore where _allSafeEvents is not serialized
        // and remains empty. Without this guard, RebuildSafeState would overwrite the
        // restored _safeProjector with an empty initial state, causing data loss.
        if (_allSafeEvents.Count == 0)
        {
            return;
        }

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
        // IMPORTANT: Don't reset the version to 0. The version should reflect the total
        // number of events processed as safe. When this is called after restoring from
        // a snapshot, _allSafeEvents only contains new events from catch-up, not all
        // historical events. We should preserve the base version count.
        // The safe version has already been incremented for each event in ProcessEvent,
        // so we don't need to recalculate it here.
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
        }

        _safeProjector = newSafeProjector;
        _safeLastEventId = newSafeLastEventId;
        _safeLastSortableUniqueId = newSafeLastSortableId;
        // Don't update _safeVersion here - it's updated in ProcessBufferedEvents
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

    private T CloneProjector(T source)
    {
        // Serialize and deserialize to create a deep clone
        var json = JsonSerializer.Serialize(source, source.GetType(), _jsonOptions);
        var cloned = JsonSerializer.Deserialize(json, source.GetType(), _jsonOptions);
        return (T)cloned!;
    }
}
