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
public class DualStateProjectionWrapper<T>
    : ISafeAndUnsafeStateAccessor<T>, IMultiProjectionPayload, IDualStateAccessor, IDualStateRebuildSignals
    where T : IMultiProjectionPayload
{
    // Keep track of safe events until the next persisted safe snapshot boundary. The key is the complete stable
    // projection order, not arrival order: SortableUniqueId can tie, so EventId is the required deterministic tiebreaker.
    private readonly SortedDictionary<SafeEventOrderKey, Event> _allSafeEvents = new();

    // Keep duplicate detection across the current in-memory history until the next compaction boundary.
    private readonly HashSet<Guid> _processedEventIds = new();

    // Buffer for events within SafeWindow (using Dictionary to handle duplicates)
    private readonly Dictionary<Guid, Event> _bufferedEvents = new();

    private readonly string _projectorName;
    private readonly ICoreMultiProjectorTypes _types;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _useIncrementalSafePromotion;

    // In the fresh (pre-compaction) path an out-of-order safe arrival is retained but not folded. Subsequent arrivals
    // must also remain unfurled until a real consumption boundary repairs the ordered history. This deliberately keeps
    // an apparent in-order tail from accidentally reintroducing the old per-arrival re-fold behaviour.
    private bool _safeHistoryDirty;
    private bool _servedStateDirty;
    private Guid? _dirtyCausingEventId;
    private string? _dirtyCausingPosition;
    private SortableUniqueId? _lastSafeWindowThreshold;
    private DcbDomainTypes? _lastDomainTypes;

    // SEK-G18 integrity-guard signal: an out-of-global-order safe promotion / arrival was observed that the incremental
    // path cannot reorder. The grain/host must respond with a full ordered rebuild from the authoritative event store.
    private bool _rebuildRequired;

    // SEK-G20: the identity of the offending event that forced the rebuild signal (out-of-global-order safe promotion on
    // a compacted baseline). Retained so the durable rebuild marker / a non-capable-store G14 fault carries FULL context
    // (projector + event id + position), not just a boolean.
    private string? _rebuildOffendingEventId;
    private string? _rebuildOffendingPosition;

    private void SignalRebuild(Event offending)
    {
        _rebuildRequired = true;
        _rebuildOffendingEventId = offending.Id.ToString();
        _rebuildOffendingPosition = offending.SortableUniqueIdValue;
    }

    // Safe state - events older than SafeWindow
    private T _safeProjector;
    private int _safeVersion;
    private readonly int _safeHistoryBaseVersion;
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
        _safeHistoryBaseVersion = initialVersion;
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
        _safeHistoryBaseVersion = initialVersion;
        _unsafeVersion = initialVersion;
        _safeLastEventId = initialLastEventId;
        _unsafeLastEventId = initialLastEventId;
        _safeLastSortableUniqueId = initialLastSortableUniqueId ?? string.Empty;
        _unsafeLastSortableUniqueId = initialLastSortableUniqueId ?? string.Empty;
    }

    public SafeProjection<T> GetSafeProjection(SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        RememberConsumptionContext(safeWindowThreshold, domainTypes);
        ProcessBufferedEvents(safeWindowThreshold, domainTypes);
        return new SafeProjection<T>(_safeProjector, _safeLastSortableUniqueId, _safeVersion);
    }

    public UnsafeProjection<T> GetUnsafeProjection(DcbDomainTypes domainTypes)
    {
        _lastDomainTypes = domainTypes;
        ConsumeServedState(_lastSafeWindowThreshold ?? ZeroThreshold, domainTypes);
        return new UnsafeProjection<T>(_unsafeProjector, _unsafeLastSortableUniqueId, _unsafeLastEventId, _unsafeVersion);
    }

    public ISafeAndUnsafeStateAccessor<T> ProcessEvent(
        Event evt,
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes)
    {
        RememberConsumptionContext(safeWindowThreshold, domainTypes);

        // Check if this event has already been processed (duplicate prevention)
        var eventTime = new SortableUniqueId(evt.SortableUniqueIdValue);
        var isInSafeWindow = !eventTime.IsEarlierThanOrEqual(safeWindowThreshold);

        if (_processedEventIds.Contains(evt.Id))
        {
            // Event already processed, skip it
            return this;
        }

        _processedEventIds.Add(evt.Id);

        if (isInSafeWindow)
        {
            // Still inside the safe window: hold it in the buffer. The served state is (safe baseline + ordered buffer).
            // Only a deferred SAFE-history repair suppresses the normal unsafe-window reconcile. An ordinary unsafe
            // out-of-order arrival must still reconcile immediately so a fold failure is captured at this ProcessEvent
            // boundary with the offending event's fault attribution.
            var inOrder = !_safeHistoryDirty && !_servedStateDirty && IsStrictlyAfterServedHead(evt);
            _bufferedEvents[evt.Id] = evt;

            if (inOrder)
            {
                ProjectUnsafeInOrder(evt, safeWindowThreshold, domainTypes);
            }
            else if (!_safeHistoryDirty)
            {
                ReconcileServedState(safeWindowThreshold, domainTypes);
                _servedStateDirty = false;
            }
            else
            {
                _servedStateDirty = true;
            }

            return this;
        }

        // Already at/under the safe threshold. It must fold onto the safe state IN GLOBAL ORDER — never out of order into
        // the accumulator.
        _allSafeEvents[SafeEventOrderKey.From(evt)] = evt;

        if (_useIncrementalSafePromotion)
        {
            if (IsStrictlyAfterSafeHead(evt))
            {
                ProjectSafeInOrder(evt, domainTypes);
                _safeVersion++;
                ReconcileServedState(safeWindowThreshold, domainTypes);
                _servedStateDirty = false;
            }
            else
            {
                // Compacted/incremental baseline: the full history is gone, so the wrapper cannot reorder locally. Signal
                // a full ordered rebuild from the authoritative event store (SEK-G18 integrity guard) rather than fold
                // out of order or rebuild a compacted baseline from the initial payload.
                SignalRebuild(evt);
                _servedStateDirty = true;
            }
            return this;
        }

        if (_safeHistoryDirty)
        {
            // A dirty fresh history is repaired only at a consumption boundary. Do not fold a later in-order arrival onto
            // the stale safe payload and do not reconcile at the end of this ProcessEvent.
            _servedStateDirty = true;
            return this;
        }

        if (IsStrictlyAfterSafeHead(evt))
        {
            // The clean fresh path keeps the established immediate served-state reconciliation. Once an out-of-order safe
            // arrival marks the history dirty, the branch above suppresses this path until a real consumption boundary.
            ProjectSafeInOrder(evt, domainTypes);
            _safeVersion++;
            ReconcileServedState(safeWindowThreshold, domainTypes);
            _servedStateDirty = false;
            return this;
        }

        // Fresh path retains the complete ordered history. Marking dirty is O(log N) insertion plus constant metadata;
        // crucially, do not rebuild or reconcile here. The next real consume performs the single repair.
        MarkSafeHistoryDirty(evt);
        return this;
    }

    // IDualStateAccessor explicit implementation — non-generic access without reflection
    int IDualStateAccessor.UnsafeVersion => _unsafeVersion;
    Guid IDualStateAccessor.UnsafeLastEventId => _unsafeLastEventId;
    string IDualStateAccessor.UnsafeLastSortableUniqueId => _unsafeLastSortableUniqueId;
    string? IDualStateAccessor.SafeLastSortableUniqueId =>
        string.IsNullOrEmpty(_safeLastSortableUniqueId) ? null : _safeLastSortableUniqueId;
    object IDualStateAccessor.GetSafeProjectorPayload()
    {
        ConsumeUsingRememberedContext();
        return _safeProjector!;
    }

    object IDualStateAccessor.GetUnsafeProjectorPayload()
    {
        ConsumeUsingRememberedContext();
        return _unsafeProjector!;
    }

    // SEK-G18 internal seam (not on the public IDualStateAccessor surface).
    bool IDualStateRebuildSignals.IsServedIdenticalToSafe =>
        !_rebuildRequired && !_safeHistoryDirty && !_servedStateDirty && _bufferedEvents.Count == 0;
    bool IDualStateRebuildSignals.RebuildRequired => _rebuildRequired;
    string? IDualStateRebuildSignals.RebuildOffendingEventId => _rebuildOffendingEventId;
    string? IDualStateRebuildSignals.RebuildOffendingPosition => _rebuildOffendingPosition;
    IDualStateAccessor IDualStateAccessor.ProcessEventAs(
        Event evt, SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        ProcessEvent(evt, safeWindowThreshold, domainTypes);
        return this;
    }

    void IDualStateAccessor.PromoteBufferedEvents(
        SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        RememberConsumptionContext(safeWindowThreshold, domainTypes);
        ProcessBufferedEvents(safeWindowThreshold, domainTypes);
    }

    void IDualStateAccessor.CompactSafeHistory()
    {
        // Compaction is a consumption boundary too. A dirty fresh history must be repaired and its payload/metadata
        // published before the history is cleared and the wrapper switches to the compacted incremental mode.
        ConsumeUsingRememberedContext();
        if (_rebuildRequired)
        {
            return;
        }

        _allSafeEvents.Clear();
        RebuildProcessedEventIdsFromBufferedEvents();
        _useIncrementalSafePromotion = true;
    }

    private void ProcessBufferedEvents(SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        RememberConsumptionContext(safeWindowThreshold, domainTypes);
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

        // Add newly safe events to the retained total-order collection. Fresh history is repaired only when it is consumed;
        // the sorted collection makes arrival insertion O(log N) rather than re-sorting and re-folding N events per arrival.
        if (eventsToProcess.Count > 0)
        {
            foreach (var ev in eventsToProcess)
            {
                _allSafeEvents[SafeEventOrderKey.From(ev)] = ev;
            }

            if (_useIncrementalSafePromotion)
            {
                ApplyEventsIncrementally(eventsToProcess, domainTypes);

                // Do not advance the safe version if the incremental guard tripped — a full rebuild will re-establish it.
                if (!_rebuildRequired)
                {
                    _safeVersion += eventsToProcess.Count;
                }

                _servedStateDirty = true;
            }
            else if (_safeHistoryDirty)
            {
                // A later promotion must not fold onto a stale safe payload after a prior out-of-order arrival. It remains
                // in the sorted history for the one deferred repair at the consumption boundary below.
                _servedStateDirty = true;
            }
            else
            {
                var orderedPromotions = eventsToProcess
                    .OrderBy(SafeEventOrderKey.From)
                    .ToList();

                if (!IsStrictlyAfterSafeHead(orderedPromotions[0]))
                {
                    MarkSafeHistoryDirty(orderedPromotions[0]);
                }
                else
                {
                    foreach (var ev in orderedPromotions)
                    {
                        ProjectSafeInOrder(ev, domainTypes);
                        _safeVersion++;
                    }

                    _servedStateDirty = true;
                }
            }
        }

        // This is deliberately unconditional, including a zero-event promotion: a dirty safe history may have been
        // created by ProcessEvent before the caller reached this consumption boundary.
        ConsumeServedState(safeWindowThreshold, domainTypes);
    }

    private void RememberConsumptionContext(SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        _lastSafeWindowThreshold = safeWindowThreshold;
        _lastDomainTypes = domainTypes;
    }

    private void ConsumeUsingRememberedContext()
    {
        if (!_safeHistoryDirty && !_servedStateDirty)
        {
            return;
        }

        var domainTypes = _lastDomainTypes ?? throw new InvalidOperationException(
            "Deferred dual-state consumption has no domain context.");
        ConsumeServedState(_lastSafeWindowThreshold ?? ZeroThreshold, domainTypes);
    }

    /// <summary>
    ///     The sole fresh-history consumption seam. A dirty arrival does no re-fold and no reconciliation; a real consumer
    ///     reaches this method to repair the sorted history, publish safe payload + metadata as one state transition, then
    ///     reconcile the served state before the caller consumes it. If either repair or reconciliation fails, the prior
    ///     published state remains intact. The replay event whose projector fold failed is attached to the original
    ///     exception for actor fault capture, while the earlier dirty-producing arrival remains separate diagnostics.
    /// </summary>
    private void ConsumeServedState(SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        if (_rebuildRequired)
        {
            return;
        }

        if (!_safeHistoryDirty)
        {
            if (_servedStateDirty)
            {
                ReconcileServedState(safeWindowThreshold, domainTypes);
                _servedStateDirty = false;
            }

            return;
        }

        var published = new PublishedState(
            _safeProjector,
            _safeVersion,
            _safeLastEventId,
            _safeLastSortableUniqueId,
            _unsafeProjector,
            _unsafeVersion,
            _unsafeLastEventId,
            _unsafeLastSortableUniqueId);

        try
        {
            var repaired = RebuildSafeState(domainTypes);

            // Publish the repaired safe payload and every accompanying metadata value together. Reconcile then publishes
            // the served projection only after the repair is complete; a reconciliation failure rolls this transition back.
            _safeProjector = repaired.Projector;
            _safeVersion = repaired.Version;
            _safeLastEventId = repaired.LastEventId;
            _safeLastSortableUniqueId = repaired.LastSortableUniqueId;

            ReconcileServedState(safeWindowThreshold, domainTypes);

            _safeHistoryDirty = false;
            _servedStateDirty = false;
            _dirtyCausingEventId = null;
            _dirtyCausingPosition = null;
        }
        catch (Exception exception)
        {
            _safeProjector = published.SafeProjector;
            _safeVersion = published.SafeVersion;
            _safeLastEventId = published.SafeLastEventId;
            _safeLastSortableUniqueId = published.SafeLastSortableUniqueId;
            _unsafeProjector = published.UnsafeProjector;
            _unsafeVersion = published.UnsafeVersion;
            _unsafeLastEventId = published.UnsafeLastEventId;
            _unsafeLastSortableUniqueId = published.UnsafeLastSortableUniqueId;
            _servedStateDirty = true;
            AttachDeferredRepairDirtyAttribution(exception);
            throw;
        }
    }

    private void MarkSafeHistoryDirty(Event causingEvent)
    {
        if (!_safeHistoryDirty)
        {
            _dirtyCausingEventId = causingEvent.Id;
            _dirtyCausingPosition = causingEvent.SortableUniqueIdValue;
        }

        _safeHistoryDirty = true;
        _servedStateDirty = true;
    }

    private void AttachDeferredRepairDirtyAttribution(Exception exception)
    {
        if (_dirtyCausingEventId is { } eventId)
        {
            DeferredSafeRepairFaultAttribution.TryAnnotate(
                exception,
                DeferredSafeRepairFaultAttribution.DirtyEventIdDataKey,
                eventId.ToString());
        }

        if (!string.IsNullOrEmpty(_dirtyCausingPosition))
        {
            DeferredSafeRepairFaultAttribution.TryAnnotate(
                exception,
                DeferredSafeRepairFaultAttribution.DirtyPositionDataKey,
                _dirtyCausingPosition);
        }
    }

    private void ProjectSafeInOrder(Event evt, DcbDomainTypes domainTypes)
    {
        var safeProjected = _types.Project(
            _projectorName,
            _safeProjector,
            evt,
            ResolveTags(evt, domainTypes),
            domainTypes,
            ZeroThreshold);
        if (!safeProjected.IsSuccess)
        {
            throw safeProjected.GetException();
        }

        _safeProjector = (T)safeProjected.GetValue();
        _safeLastEventId = evt.Id;
        _safeLastSortableUniqueId = evt.SortableUniqueIdValue;
    }

    private void ProjectUnsafeInOrder(Event evt, SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes)
    {
        var served = _types.Project(
            _projectorName,
            _unsafeProjector,
            evt,
            ResolveTags(evt, domainTypes),
            domainTypes,
            safeWindowThreshold);
        if (!served.IsSuccess)
        {
            throw served.GetException();
        }

        _unsafeProjector = (T)served.GetValue();
        _unsafeLastEventId = evt.Id;
        _unsafeLastSortableUniqueId = evt.SortableUniqueIdValue;
        _unsafeVersion = _safeVersion + _bufferedEvents.Count;
    }

    private static List<ITag> ResolveTags(Event evt, DcbDomainTypes domainTypes) =>
        evt.Tags.Select(tagString => domainTypes.TagTypes.GetTag(tagString)).ToList();

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
                .OrderBy(SafeEventOrderKey.From)
                .ToList();
            foreach (var ev in ordered)
            {
                var projected = _types.Project(
                    _projectorName,
                    served,
                    ev,
                    ResolveTags(ev, domainTypes),
                    domainTypes,
                    safeWindowThreshold);
                if (!projected.IsSuccess)
                {
                    var exception = projected.GetException();
                    DeferredSafeRepairFaultAttribution.AnnotateReplayEvent(exception, ev);
                    throw exception;
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

    private bool IsStrictlyAfterSafeHead(Event evt) =>
        string.IsNullOrEmpty(_safeLastSortableUniqueId)
        || SafeEventOrderKey.From(evt).CompareTo(
            new SafeEventOrderKey(_safeLastSortableUniqueId, _safeLastEventId)) > 0;

    private bool IsStrictlyAfterServedHead(Event evt) =>
        string.IsNullOrEmpty(_unsafeLastSortableUniqueId)
        || SafeEventOrderKey.From(evt).CompareTo(
            new SafeEventOrderKey(_unsafeLastSortableUniqueId, _unsafeLastEventId)) > 0;

    private static readonly SortableUniqueId ZeroThreshold =
        new("000000000000000000000000000000000000000000000000");

    private readonly record struct SafeEventOrderKey(string SortableUniqueId, Guid EventId)
        : IComparable<SafeEventOrderKey>
    {
        public static SafeEventOrderKey From(Event evt) => new(evt.SortableUniqueIdValue, evt.Id);

        public int CompareTo(SafeEventOrderKey other)
        {
            var position = string.Compare(SortableUniqueId, other.SortableUniqueId, StringComparison.Ordinal);
            return position != 0 ? position : EventId.CompareTo(other.EventId);
        }
    }

    private readonly record struct RepairedSafeState(
        T Projector,
        int Version,
        Guid LastEventId,
        string LastSortableUniqueId);

    private readonly record struct PublishedState(
        T SafeProjector,
        int SafeVersion,
        Guid SafeLastEventId,
        string SafeLastSortableUniqueId,
        T UnsafeProjector,
        int UnsafeVersion,
        Guid UnsafeLastEventId,
        string UnsafeLastSortableUniqueId);

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

    private RepairedSafeState RebuildSafeState(DcbDomainTypes domainTypes)
    {
        if (_allSafeEvents.Count == 0)
        {
            return new RepairedSafeState(
                _safeProjector,
                _safeVersion,
                _safeLastEventId,
                _safeLastSortableUniqueId);
        }

        var rebuiltProjector = _types.GenerateInitialPayload(_projectorName);
        if (!rebuiltProjector.IsSuccess)
        {
            throw rebuiltProjector.GetException();
        }

        var newSafeProjector = (T)rebuiltProjector.GetValue();
        var newSafeLastEventId = Guid.Empty;
        var newSafeLastSortableId = string.Empty;

        foreach (var ev in _allSafeEvents.Values)
        {
            var projected = _types.Project(
                _projectorName,
                newSafeProjector,
                ev,
                ResolveTags(ev, domainTypes),
                domainTypes,
                ZeroThreshold);

            if (!projected.IsSuccess)
            {
                var exception = projected.GetException();
                DeferredSafeRepairFaultAttribution.AnnotateReplayEvent(exception, ev);
                throw exception;
            }

            newSafeProjector = (T)projected.GetValue();
            newSafeLastEventId = ev.Id;
            newSafeLastSortableId = ev.SortableUniqueIdValue;
        }

        return new RepairedSafeState(
            newSafeProjector,
            _safeHistoryBaseVersion + _allSafeEvents.Count,
            newSafeLastEventId,
            newSafeLastSortableId);
    }

    private void ApplyEventsIncrementally(List<Event> events, DcbDomainTypes domainTypes)
    {
        events.Sort((a, b) => SafeEventOrderKey.From(a).CompareTo(SafeEventOrderKey.From(b)));

        foreach (var ev in events)
        {
            // SEK-G18 integrity guard: an incrementally-promoted event MUST sort strictly after the held safe head.
            // If it does not, the incremental (fold-onto-baseline) path cannot reorder it into the safe payload, so signal
            // a full ordered rebuild rather than folding it out of global order.
            if (!IsStrictlyAfterSafeHead(ev))
            {
                SignalRebuild(ev);
                return;
            }

            ProjectSafeInOrder(ev, domainTypes);
        }
    }

    private static T CloneProjector(T source, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(source, source.GetType(), options);
        var cloned = JsonSerializer.Deserialize(json, source.GetType(), options);
        return (T)cloned!;
    }
}

/// <summary>
///     Internal provenance exchanged by the deferred-repair wrapper and its actor host. It deliberately lives outside
///     serialized projection state and public accessor contracts: the original projector exception remains the thing
///     callers receive, with only diagnostic <see cref="Exception.Data" /> attached to it.
/// </summary>
internal static class DeferredSafeRepairFaultAttribution
{
    internal const string ReplayEventIdDataKey = "DeferredSafeRepairEventId";
    internal const string ReplayEventTypeDataKey = "DeferredSafeRepairEventType";
    internal const string ReplayPositionDataKey = "DeferredSafeRepairPosition";
    internal const string DirtyEventIdDataKey = "DeferredSafeRepairDirtyEventId";
    internal const string DirtyPositionDataKey = "DeferredSafeRepairDirtyPosition";

    internal static void AnnotateReplayEvent(Exception exception, Event replayEvent)
    {
        Annotate(exception, ReplayEventIdDataKey, replayEvent.Id.ToString());
        Annotate(exception, ReplayEventTypeDataKey, replayEvent.EventType);
        Annotate(exception, ReplayPositionDataKey, replayEvent.SortableUniqueIdValue);
    }

    internal static bool TryGetReplayEvent(
        Exception exception,
        out Guid eventId,
        out string eventType,
        out string position)
    {
        if (TryGetReplayEventCore(exception, out eventId, out eventType, out position))
        {
            return true;
        }

        return exception.InnerException is not null &&
               TryGetReplayEventCore(exception.InnerException, out eventId, out eventType, out position);
    }

    internal static void TryAnnotate(Exception exception, string key, string value)
    {
        Annotate(exception, key, value);
    }

    private static bool TryGetReplayEventCore(
        Exception exception,
        out Guid eventId,
        out string eventType,
        out string position)
    {
        eventId = Guid.Empty;
        eventType = string.Empty;
        position = string.Empty;

        try
        {
            var eventIdText = exception.Data[ReplayEventIdDataKey] as string;
            eventType = exception.Data[ReplayEventTypeDataKey] as string ?? string.Empty;
            position = exception.Data[ReplayPositionDataKey] as string ?? string.Empty;
            return Guid.TryParse(eventIdText, out eventId) &&
                   !string.IsNullOrEmpty(eventType) &&
                   !string.IsNullOrEmpty(position);
        }
        catch (Exception)
        {
            eventId = Guid.Empty;
            eventType = string.Empty;
            position = string.Empty;
            return false;
        }
    }

    private static void Annotate(Exception exception, string key, string value)
    {
        TryAnnotateOne(exception, key, value);

        // Actor fault capture intentionally unwraps legacy boundary exceptions before it records the descriptor. Keep
        // the provenance with both layers when a projector implementation supplied one, without changing either type
        // or stack semantics.
        if (exception.InnerException is not null)
        {
            TryAnnotateOne(exception.InnerException, key, value);
        }
    }

    private static void TryAnnotateOne(Exception exception, string key, string value)
    {
        try
        {
            if (!exception.Data.IsReadOnly && !exception.Data.Contains(key))
            {
                exception.Data[key] = value;
            }
        }
        catch (Exception)
        {
            // Provenance is diagnostic and must never replace the projector failure itself.
        }
    }
}
