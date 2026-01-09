using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.Room;

public record RoomUpdated(
    Guid RoomId,
    string Name,
    int Capacity,
    string Location,
    List<string> Equipment,
    bool RequiresApproval) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new RoomTag(RoomId));
}
