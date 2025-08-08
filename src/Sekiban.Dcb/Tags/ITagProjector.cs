using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Tags;

public interface ITagProjector
{
    /// <summary>
    /// Returns the tag group name this projector targets.
    /// </summary>
    /// <returns>Tag group name.</returns>
    string ForTagGroupName();

    string GetProjectorVersion();
    ITagStatePayload Project(ITagStatePayload current, Event ev);
}
