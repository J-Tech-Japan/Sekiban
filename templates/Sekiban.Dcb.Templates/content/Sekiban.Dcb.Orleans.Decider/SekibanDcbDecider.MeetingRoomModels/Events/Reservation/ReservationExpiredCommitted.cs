using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Events.Reservation;

/// <summary>
///     Event for when a reservation expires (hold or approval expired)
/// </summary>
public record ReservationExpiredCommitted(
    Guid ReservationId,
    Guid RoomId,
    DateTime StartTime,
    DateTime EndTime,
    DateTime ExpiredAt,
    string Reason,
    Guid? PoolId,
    Guid? HoldId,
    Guid? ApprovalRequestId) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags()
    {
        var tags = new List<ITag>
        {
            new ReservationTag(ReservationId),
            new RoomTag(RoomId)
        };

        if (ApprovalRequestId.HasValue)
        {
            tags.Add(new ApprovalRequestTag(ApprovalRequestId.Value));
        }

        // Add RoomDailyActivityTag for each day to update the daily state
        tags.AddRange(RoomDailyActivityTag.CreateTagsForTimeRange(RoomId, StartTime, EndTime));

        return new EventPayloadWithTags(this, tags);
    }
}
