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

    // SEK-G18 integrity-guard signal: an out-of-global-order safe promotion / arrival was observed that the incremental
    // path cannot reorder. The grain/host must respond with a full ordered rebuild from the authoritative event store.
    private bool _rebuildRequired;

    // Safe state - events older than SafeWindow
    private T _safeProjector;
    private int _safeVersion;
    private Guid _safeLastEventId;
    private string _safeLastSortableUniqueId = string.Empty;

    // Unsafe (served) state - the safe baseline reconciled with the still-buffered events in global SortableUniqueId order.
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

        if (_processedEventIds.Contains(evt.Id))
        {
            // Event already processed, skip it
            return this;
        }

        _processedEventIds.Add(evt.Id);

        var tags = evt.Tags.Select(tagString => domainTypes.TagTypes.GetTag(tagString)).ToList();

        if (isInSafeWindow)
        {
            // Still inside the safe window: hold it in the buffer. The served state is (safe baseline + ordered buffer),
            // so an in-order arrival can be folded onto the served accumulator directly; an out-of-order arrival forces a
            // re-derivation so the served state never depends on arrival order.
            var inOrder = IsStrictlyAfterServedHead(evt.SortableUniqueIdValue);
            _bufferedEvents[evt.Id] = evt;

            if (inOrder)
            {
                var served = _types.Project(_projectorName, _unsafeProjector, evt, tags, domainTypes, safeWindowThreshold);
                if (!served.IsSuccess)
                {
                    throw served.GetException();
                }
                _unsafeProjector = (T)served.GetValue();
                _unsafeLastEventId = evt.Id;
                _unsafeLastSortableUniqueId = evt.SortableUniqueIdValue;
                _unsafeVersion = _safeVersion + _bufferedEvents.Count;
            }
            else
            {
                ReconcileServedState(safeWindowThreshold, domainTypes);
            }

            return this;
        }

        // Already at/under the safe threshold. It must fold onto the safe state IN GLOBAL ORDER — never out of order into
        // the accumulator.
        _allSafeEvents[evt.Id] = evt;

        if (IsStrictlyAfterSafeHead(evt.SortableUniqueIdValue))
        {
            // In order: fold directly onto the safe baseline.
            var safeProjected = _types.Project(_projectorName, _safeProjector, evt, tags, domainTypes, ZeroThreshold);
            if (!safeProjected.IsSuccess)
            {
                throw safeProjected.GetException();
            }
            _safeProjector = (T)safeProjected.GetValue();
            _safeLastEventId = evt.Id;
            _safeLastSortableUniqueId = evt.SortableUniqueIdValue;
            _safeVersion++;
        }
        else if (!_useIncrementalSafePromotion)
        {
            // Fresh path still retains the FULL safe history in _allSafeEvents, so a global re-sort places the
            // out-of-order arrival correctly (no compacted baseline, no data loss).
            RebuildSafeState(domainTypes);
            _safeVersion++;
        }
        else
        {
            // Compacted/incremental baseline: the full history is gone, so the wrapper cannot reorder locally. Signal a
            // full ordered rebuild from the authoritative event store (SEK-G18 integrity guard) rather than fold out of
            // order or rebuild a compacted baseline from the initial payload.
            _rebuildRequired = true;
            return this;
        }

        // The safe baseline advanced; re-derive the served state from it plus the still-buffered events.
        ReconcileServedState(safeWindowThreshold, domainTypes);
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
    bool IDualStateAccessor.IsServedIdenticalToSafe => !_rebuildRequired && _bufferedEvents.Count == 0;
    bool IDualStateAccessor.RebuildRequired => _rebuildRequired;
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

            // Do not advance the safe version if the incremental guard tripped — a full rebuild will re-establish it.
            if (!_rebuildRequired)
            {
                _safeVersion += eventsToProcess.Count;
            }
        }

        // Re-derive the served (unsafe) state = safe baseline + still-buffered events in global SortableUniqueId order.
        // Reads promote before reading, so this keeps the served payload converged and IsSafeState truthful.
        ReconcileServedState(safeWindowThreshold, domainTypes);
    }

    /// <summary>
    ///     SEK-G18 graduation reconcile: re-derive the served (unsafe) state as the safe baseline plus the still-buffered
    ///     events replayed in global SortableUniqueId ordinal order, then publish payload / last-event / position / version
    ///     atomically. Because projector folds are pure and payloads immutable, folding onto <c>_safeProjector</c> never
    ///     mutates it — this is the "clone(safe) + ordered buffer" derivation. When a rebuild is pending the served state is
    ///     left untouched (queries are gated on the rebuild by the grain/host).
    /// </summary>
    private void ReconcileServedState(SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        if (_rebuildRequired)
        {
            return;
        }

        var served = _safeProjector;
        var lastEventId = _safeLastEventId;
        var lastSortableId = _safeLastSortableUniqueId;

        if (_bufferedEvents.Count > 0)
        {
            var ordered = _bufferedEvents.Values
                .OrderBy(ev => ev.SortableUniqueIdValue, StringComparer.Ordinal)
                .ToList();
            foreach (var ev in ordered)
            {
                var tags = ev.Tags.Select(tagString => domainTypes.TagTypes.GetTag(tagString)).ToList();
                var projected = _types.Project(_projectorName, served, ev, tags, domainTypes, safeWindowThreshold);
                if (!projected.IsSuccess)
                {
                    throw projected.GetException();
                }
                served = (T)projected.GetValue();
                lastEventId = ev.Id;
                lastSortableId = ev.SortableUniqueIdValue;
            }
        }

        _unsafeProjector = served;
        _unsafeLastEventId = lastEventId;
        _unsafeLastSortableUniqueId = lastSortableId;
        _unsafeVersion = _safeVersion + _bufferedEvents.Count;
    }

    private bool IsStrictlyAfterSafeHead(string sortableUniqueId) =>
        string.IsNullOrEmpty(_safeLastSortableUniqueId)
        || string.Compare(sortableUniqueId, _safeLastSortableUniqueId, StringComparison.Ordinal) > 0;

    private bool IsStrictlyAfterServedHead(string sortableUniqueId) =>
        string.IsNullOrEmpty(_unsafeLastSortableUniqueId)
        || string.Compare(sortableUniqueId, _unsafeLastSortableUniqueId, StringComparison.Ordinal) > 0;

    private static readonly SortableUniqueId ZeroThreshold =
        new("000000000000000000000000000000000000000000000000");

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
            // SEK-G18 integrity guard: an incrementally-promoted event MUST sort strictly after the held safe head.
            // If it does not, the incremental (fold-onto-baseline) path cannot reorder it into the safe payload, so signal
            // a full ordered rebuild rather than folding it out of global order.
            if (!IsStrictlyAfterSafeHead(ev.SortableUniqueIdValue))
            {
                _rebuildRequired = true;
                return;
            }

            var tags = ev.Tags.Select(tagString => domainTypes.TagTypes.GetTag(tagString)).ToList();
            var projected = _types.Project(
                _projectorName,
                _safeProjector,
                ev,
                tags,
                domainTypes,
                ZeroThreshold);

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
