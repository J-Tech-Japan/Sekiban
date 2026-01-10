using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Events.Reservation;

public record ReservationCancelled(
    Guid ReservationId,
    Guid RoomId,
    DateTime StartTime,
    DateTime EndTime,
    string Reason,
    DateTime CancelledAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags()
    {
        var tags = new List<ITag>
        {
            new ReservationTag(ReservationId),
            new RoomTag(RoomId)
        };

        // Add RoomDailyActivityTag for each day to update the daily state
        tags.AddRange(RoomDailyActivityTag.CreateTagsForTimeRange(RoomId, StartTime, EndTime));

        return new EventPayloadWithTags(this, tags);
    }
}
