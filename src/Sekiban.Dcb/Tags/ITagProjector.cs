using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Tags;

public interface ITagProjector
{
    string GetTagProjectorName();
    ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload);
}