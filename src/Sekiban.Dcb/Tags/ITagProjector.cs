using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Tags;

public interface ITagProjector
{
    string GetProjectorVersion();
    ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload);
}