using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

public readonly record struct SafeProjection<T>(T State, string SafeLastSortableUniqueId, int Version) where T : IMultiProjectionPayload;
public readonly record struct UnsafeProjection<T>(T State, string LastSortableUniqueId, Guid LastEventId, int Version) where T : IMultiProjectionPayload;

/// <summary>
/// Interface for projections that expose safe/unsafe views with explicit metadata
/// </summary>
/// <typeparam name="T">payload type</typeparam>
public interface ISafeAndUnsafeStateAccessor<T> where T : IMultiProjectionPayload
{
    SafeProjection<T> GetSafeProjection(SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes);
    UnsafeProjection<T> GetUnsafeProjection(DcbDomainTypes domainTypes);
    ISafeAndUnsafeStateAccessor<T> ProcessEvent(Event evt, SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes);
}
