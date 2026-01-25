using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Room;

/// <summary>
///     Projector for RoomReservationsState.
///     Tracks held and confirmed reservations per room for conflict detection.
/// </summary>
public class RoomReservationsProjector : ITagProjector<RoomReservationsProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(RoomReservationsProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as RoomReservationsState ?? RoomReservationsState.Empty;

        return ev.Payload switch
        {
            ReservationHoldCommitted committed => state.AddOrUpdateReservation(
                committed.ReservationId,
                committed.StartTime,
                committed.EndTime,
                committed.Purpose,
                committed.OrganizerId,
                ReservationSlotStatus.Held),
            ReservationConfirmed confirmed => state.AddOrUpdateReservation(
                confirmed.ReservationId,
                confirmed.StartTime,
                confirmed.EndTime,
                confirmed.Purpose,
                confirmed.OrganizerId,
                ReservationSlotStatus.Confirmed),
            ReservationCancelled cancelled => state.RemoveReservation(cancelled.ReservationId),
            ReservationRejected rejected => state.RemoveReservation(rejected.ReservationId),
            ReservationExpiredCommitted expired => state.RemoveReservation(expired.ReservationId),
            _ => state
        };
    }
}
