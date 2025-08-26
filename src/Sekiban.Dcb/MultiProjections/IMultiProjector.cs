using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Generic interface for multi projectors with static members
/// </summary>
public interface IMultiProjector<T> : IMultiProjectionPayload where T : IMultiProjector<T>
{
    static abstract string MultiProjectorName { get; }
    static abstract string MultiProjectorVersion { get; }
    /// <summary>
    ///     Project with tags support for tag-based filtering
    /// </summary>
    static abstract ResultBox<T> Project(T payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes);

    static abstract T GenerateInitialPayload();
}
