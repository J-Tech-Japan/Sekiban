using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Helper class for creating Event objects in tests
/// </summary>
public static class EventTestHelper
{
    /// <summary>
    ///     Creates an Event from an event payload and tags
    /// </summary>
    public static Event CreateEvent(IEventPayload payload, List<ITagCommon> tags)
    {
        var eventId = Guid.NewGuid();
        var sortableId = SortableUniqueId.GenerateNew();
        var metadata = new EventMetadata(eventId.ToString(), eventId.ToString(), "TestUser");

        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            eventId,
            metadata,
            tags.Select(t => t.GetTag()).ToList());
    }

    /// <summary>
    ///     Creates an Event from an event payload and a single tag
    /// </summary>
    public static Event CreateEvent(IEventPayload payload, ITagCommon tag) => CreateEvent(payload, new List<ITagCommon> { tag });
}
