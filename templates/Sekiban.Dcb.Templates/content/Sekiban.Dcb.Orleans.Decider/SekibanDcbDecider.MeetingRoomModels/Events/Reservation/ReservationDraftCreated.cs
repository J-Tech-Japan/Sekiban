using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Events.Reservation;

public record ReservationDraftCreated(
    Guid ReservationId,
    Guid RoomId,
    Guid OrganizerId,
    string OrganizerName,
    DateTime StartTime,
    DateTime EndTime,
    string Purpose,
    List<string>? SelectedEquipment = null) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags()
    {
        var tags = new List<ITag>
        {
            new ReservationTag(ReservationId),
            new RoomTag(RoomId),
            UserMonthlyReservationTag.FromStartTime(OrganizerId, StartTime)
        };

        return new EventPayloadWithTags(this, tags);
    }
}
