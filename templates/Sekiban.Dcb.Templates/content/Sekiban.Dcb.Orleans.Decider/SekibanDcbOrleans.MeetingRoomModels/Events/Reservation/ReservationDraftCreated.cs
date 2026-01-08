using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.Reservation;

public record ReservationDraftCreated(
    Guid ReservationId,
    Guid RoomId,
    Guid OrganizerId,
    string OrganizerName,
    DateTime StartTime,
    DateTime EndTime,
    string Purpose) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new ReservationTag(ReservationId), new RoomTag(RoomId)]);
}
