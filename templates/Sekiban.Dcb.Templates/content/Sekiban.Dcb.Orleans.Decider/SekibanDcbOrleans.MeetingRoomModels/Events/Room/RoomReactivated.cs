using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.Room;

public record RoomReactivated(
    Guid RoomId,
    string? Reason,
    DateTime ReactivatedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new RoomTag(RoomId));
}
