using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Events.Reservation;

public record ReservationConfirmed(
    Guid ReservationId,
    Guid RoomId,
    Guid OrganizerId,
    DateTime StartTime,
    DateTime EndTime,
    string Purpose,
    DateTime ConfirmedAt,
    string? ApprovalDecisionComment) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags()
    {
        var tags = new List<ITag>
        {
            new ReservationTag(ReservationId),
            new RoomTag(RoomId)
        };

        // Add RoomDailyActivityTag for each day the reservation spans
        tags.AddRange(RoomDailyActivityTag.CreateTagsForTimeRange(RoomId, StartTime, EndTime));

        return new EventPayloadWithTags(this, tags);
    }
}
