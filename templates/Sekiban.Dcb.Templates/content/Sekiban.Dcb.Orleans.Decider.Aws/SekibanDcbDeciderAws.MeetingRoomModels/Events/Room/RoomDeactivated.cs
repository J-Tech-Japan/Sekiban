using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.Room;

public record RoomDeactivated(
    Guid RoomId,
    string? Reason,
    DateTime DeactivatedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new RoomTag(RoomId));
}
