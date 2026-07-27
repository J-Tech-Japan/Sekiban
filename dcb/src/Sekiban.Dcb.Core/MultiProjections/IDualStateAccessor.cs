using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Non-generic accessor for DualStateProjectionWrapper{T} internal state.
///     Eliminates the need for reflection when the generic type parameter T
///     is unknown at compile time.
/// </summary>
public interface IDualStateAccessor
{
    int SafeVersion { get; }
    int UnsafeVersion { get; }
    Guid UnsafeLastEventId { get; }
    string UnsafeLastSortableUniqueId { get; }
    string? SafeLastSortableUniqueId { get; }
    object GetSafeProjectorPayload();
    object GetUnsafeProjectorPayload();

    IDualStateAccessor ProcessEventAs(
        Event evt,
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes);

    /// <summary>
    ///     Promotes buffered events that have moved past the safe window threshold.
    ///     Triggers the internal ProcessBufferedEvents logic without reflection.
    /// </summary>
    void PromoteBufferedEvents(
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes);

    /// <summary>
    ///     Drops retained safe-event history after a safe snapshot has been persisted.
    ///     The current safe projector remains the new baseline.
    /// </summary>
    void CompactSafeHistory();
}

/// <summary>
///     SEK-G18 INTERNAL seam (no public API change): the dual-state wrapper surfaces the reconcile fact and the
///     out-of-global-order integrity signal to the in-repo actor without adding members to the public
///     <see cref="IDualStateAccessor" /> (which external code implements). External <see cref="IDualStateAccessor" />
///     implementors do not implement this seam, so the actor falls back safely when the cast misses.
/// </summary>
internal interface IDualStateRebuildSignals
{
    /// <summary>
    ///     True when the served (unsafe) state was published identical to the safe state — no events remain buffered and no
    ///     rebuild is pending. The actor derives a TRUTHFUL <c>IsSafeState</c> from this fact, not a timestamp comparison.
    /// </summary>
    bool IsServedIdenticalToSafe { get; }

    /// <summary>
    ///     Set when an incremental safe promotion / already-under-threshold arrival was observed OUT of global
    ///     SortableUniqueId order versus the held safe head — which the incremental path cannot reorder. The mandated remedy
    ///     is a full ordered rebuild from the authoritative event store; the wrapper never folds it out of order.
    /// </summary>
    bool RebuildRequired { get; }
}
