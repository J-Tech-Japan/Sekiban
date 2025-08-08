using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// Type-safe multi projector over events.
/// </summary>
public interface IMultiProjector<T> where T : IMultiProjectionPayload
{
    string GetVersion();
    IMultiProjectionPayload Project(IMultiProjectionPayload current, Event ev);
}
