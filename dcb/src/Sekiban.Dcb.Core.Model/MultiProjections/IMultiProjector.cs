using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Core interface for multi projectors with static members (ResultBox-based)
/// </summary>
public interface ICoreMultiProjector<T> : IMultiProjectionPayload where T : ICoreMultiProjector<T>
{
    static abstract string MultiProjectorName { get; }
    static abstract string MultiProjectorVersion { get; }
    /// <summary>
    ///     Projects one event with tags support for tag-based filtering. Implementations must not mutate
    ///     <paramref name="payload" /> and should return a new payload instance. A payload that deliberately mutates
    ///     and returns its input must implement <see cref="IMutatesProjectionInput" /> so safe and unsafe state can be
    ///     isolated. Such a projector must also return a fresh instance from <c>GenerateInitialPayload</c> on every call.
    /// </summary>
    /// <param name="payload">Current projector payload</param>
    /// <param name="ev">Event to project</param>
    /// <param name="tags">Parsed tags for the event</param>
    /// <param name="domainTypes">Domain type registry</param>
    /// <param name="safeWindowThreshold">Externally supplied safe window threshold</param>
    static abstract ResultBox<T> Project(
        T payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold);

    static abstract T GenerateInitialPayload();

}
