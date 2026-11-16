using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Generic interface for multi projectors with static members (ResultBox-based error handling).
///     The projector returns a <see cref="ResultBox{TValue}" />; the exception-based core contract
///     <see cref="ICoreMultiProjector{T}" /> is implemented here by unwrapping that box once, so a failed box
///     surfaces in the core as the exception it carries.
/// </summary>
public interface IMultiProjector<T> : ICoreMultiProjector<T> where T : IMultiProjector<T>
{
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
    new static abstract ResultBox<T> Project(
        T payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold);

    static T ICoreMultiProjector<T>.Project(
        T payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold) =>
        ResultBoxBridge.Unwrap(T.Project(payload, ev, tags, domainTypes, safeWindowThreshold));
}
