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

    /// <summary>
    ///     SEK-G18: true when the served (unsafe) state was published identical to the safe state — i.e. no events remain
    ///     buffered and no rebuild is pending. The actor derives a TRUTHFUL <c>IsSafeState</c> from this reconcile fact,
    ///     never from a timestamp comparison alone.
    /// </summary>
    bool IsServedIdenticalToSafe { get; }

    /// <summary>
    ///     SEK-G18 integrity guard: set when an incremental safe promotion (or an already-under-threshold arrival) was
    ///     observed OUT of global SortableUniqueId order versus the held safe head — which the incremental path cannot
    ///     reorder. The single mandated remedy is a full ordered rebuild from the authoritative event store driven by the
    ///     grain/host; the wrapper never folds the out-of-order event into the safe accumulator.
    /// </summary>
    bool RebuildRequired { get; }

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
