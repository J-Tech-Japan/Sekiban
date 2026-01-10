using Dcb.MeetingRoomModels.Events.Reservation;
namespace Dcb.MeetingRoomModels.States.Reservation.Deciders;

/// <summary>
///     Decider for ReservationExpiredCommitted event
/// </summary>
public static class ReservationExpiredDecider
{
    /// <summary>
    ///     Apply ReservationExpiredCommitted event to ReservationState
    /// </summary>
    public static ReservationState Evolve(this ReservationState state, ReservationExpiredCommitted expired) =>
        state switch
        {
            ReservationState.ReservationDraft draft => new ReservationState.ReservationExpired(
                draft.ReservationId,
                draft.RoomId,
                expired.Reason,
                expired.ExpiredAt),
            ReservationState.ReservationHeld held => new ReservationState.ReservationExpired(
                held.ReservationId,
                held.RoomId,
                expired.Reason,
                expired.ExpiredAt),
            _ => state // Idempotency: ignore if in other states
        };
}
