using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Exception-based interface for multi projectors with static members.
///     Throws exceptions on errors instead of returning ResultBox.
/// </summary>
public interface IMultiProjector<T> : IMultiProjectionPayload where T : IMultiProjector<T>
{
    static abstract string MultiProjectorName { get; }
    static abstract string MultiProjectorVersion { get; }

    /// <summary>
    ///     Project with tags support for tag-based filtering
    /// </summary>
    /// <param name="payload">Current projector payload</param>
    /// <param name="ev">Event to project</param>
    /// <param name="tags">Parsed tags for the event</param>
    /// <param name="domainTypes">Domain type registry</param>
    /// <param name="safeWindowThreshold">Externally supplied safe window threshold</param>
    static abstract T Project(
        T payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold);

    static abstract T GenerateInitialPayload();
}
