using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// Interface for projections that expose safe/unsafe views with explicit metadata
/// </summary>
/// <typeparam name="T">payload type</typeparam>
public interface ISafeAndUnsafeStateAccessor<T> where T : IMultiProjectionPayload
{
    SafeProjection<T> GetSafeProjection(SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes);
    UnsafeProjection<T> GetUnsafeProjection(DcbDomainTypes domainTypes);
    ISafeAndUnsafeStateAccessor<T> ProcessEvent(Event evt, SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes);
    /// <summary>
    ///     Number of events that have been incorporated into the safe (window-cleared) projection state.
    /// </summary>
    int SafeVersion { get; }
}
