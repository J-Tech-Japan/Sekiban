using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// Generic specialisation of multi projector with a fixed payload type.
/// </summary>
public interface IMultiProjector<T> : IMultiProjectorCommon where T : notnull
{
    /// <summary>
    /// Project with tags support for tag-based filtering
    /// </summary>
    ResultBox<T> Project(T payload, Event ev, List<ITag> tags);
    
    static abstract T GenerateInitialPayload();
    static abstract string GetMultiProjectorName();
}
