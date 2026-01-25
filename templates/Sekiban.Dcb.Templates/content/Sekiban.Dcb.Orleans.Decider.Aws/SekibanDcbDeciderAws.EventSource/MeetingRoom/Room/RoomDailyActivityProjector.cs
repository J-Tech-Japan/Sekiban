using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Room;

/// <summary>
///     Projector for RoomDailyActivityState.
///     Tracks confirmed reservations per room per day for conflict detection.
/// </summary>
public class RoomDailyActivityProjector : ITagProjector<RoomDailyActivityProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(RoomDailyActivityProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as RoomDailyActivityState ?? RoomDailyActivityState.Empty;

        return ev.Payload switch
        {
            ReservationConfirmed confirmed => ProjectConfirmed(state, confirmed, ev),
            ReservationCancelled cancelled => state.RemoveReservation(cancelled.ReservationId),
            ReservationRejected rejected => state.RemoveReservation(rejected.ReservationId),
            ReservationExpiredCommitted expired => state.RemoveReservation(expired.ReservationId),
            _ => state
        };
    }

    private static RoomDailyActivityState ProjectConfirmed(
        RoomDailyActivityState state,
        ReservationConfirmed confirmed,
        Event ev)
    {
        // The event now carries the full reservation details
        return state.AddReservation(
            confirmed.ReservationId,
            confirmed.StartTime,
            confirmed.EndTime,
            confirmed.Purpose,
            confirmed.OrganizerId);
    }
}
