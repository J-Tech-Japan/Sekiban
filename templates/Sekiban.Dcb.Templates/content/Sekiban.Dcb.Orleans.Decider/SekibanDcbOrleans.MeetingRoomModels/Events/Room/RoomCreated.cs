using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.Room;

public record RoomCreated(
    Guid RoomId,
    string Name,
    int Capacity,
    string Location,
    List<string> Equipment) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new RoomTag(RoomId));
}
