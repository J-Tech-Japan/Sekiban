using DcbLib.Events;

namespace DcbLib.Tags;

public interface ITagProjector
{
    string GetTagProjectorName();
    ITagStatePayload Project(ITagStatePayload current, Event ev);
}