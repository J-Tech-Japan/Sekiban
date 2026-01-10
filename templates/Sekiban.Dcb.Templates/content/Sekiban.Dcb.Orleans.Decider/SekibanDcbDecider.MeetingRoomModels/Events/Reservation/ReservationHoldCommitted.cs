using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.Reservation;

public record ReservationHoldCommitted(
    Guid ReservationId,
    Guid RoomId,
    Guid OrganizerId,
    string OrganizerName,
    DateTime StartTime,
    DateTime EndTime,
    string Purpose,
    bool RequiresApproval,
    Guid? ApprovalRequestId,
    string? ApprovalRequestComment,
    List<string>? SelectedEquipment = null) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new ReservationTag(ReservationId), new RoomTag(RoomId)]);
}
