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
}
