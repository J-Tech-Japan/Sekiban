using Dcb.MeetingRoomModels.Events.Reservation;
namespace Dcb.MeetingRoomModels.States.Reservation.Deciders;

/// <summary>
///     Decider for ReservationCancelled event
/// </summary>
public static class ReservationCancelledDecider
{
    /// <summary>
    ///     Apply ReservationCancelled event to ReservationState
    /// </summary>
    public static ReservationState Evolve(this ReservationState state, ReservationCancelled cancelled) =>
        state switch
        {
            ReservationState.ReservationDraft draft => new ReservationState.ReservationCancelled(
                draft.ReservationId,
                draft.RoomId,
                cancelled.Reason,
                cancelled.CancelledAt),
            ReservationState.ReservationHeld held => new ReservationState.ReservationCancelled(
                held.ReservationId,
                held.RoomId,
                cancelled.Reason,
                cancelled.CancelledAt),
            ReservationState.ReservationConfirmed confirmed => new ReservationState.ReservationCancelled(
                confirmed.ReservationId,
                confirmed.RoomId,
                cancelled.Reason,
                cancelled.CancelledAt),
            _ => state // Idempotency: ignore if already cancelled/rejected
        };
}
