using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.Reservation;

public record ReservationHoldCommitted(
    Guid ReservationId,
    Guid RoomId,
    bool RequiresApproval,
    Guid? ApprovalRequestId) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new ReservationTag(ReservationId), new RoomTag(RoomId)]);
}
