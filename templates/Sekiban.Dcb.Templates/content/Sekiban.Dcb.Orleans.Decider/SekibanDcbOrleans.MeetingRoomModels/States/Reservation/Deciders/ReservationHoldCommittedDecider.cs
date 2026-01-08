using Dcb.MeetingRoomModels.Events.Reservation;
namespace Dcb.MeetingRoomModels.States.Reservation.Deciders;

/// <summary>
///     Decider for ReservationHoldCommitted event
/// </summary>
public static class ReservationHoldCommittedDecider
{
    /// <summary>
    ///     Validate preconditions for committing hold
    /// </summary>
    public static void Validate(this ReservationState.ReservationDraft state)
    {
        if (state.StartTime <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Cannot commit hold for past or current time");
        }

        if (state.EndTime <= state.StartTime)
        {
            throw new InvalidOperationException("End time must be after start time");
        }
    }

    /// <summary>
    ///     Apply ReservationHoldCommitted event to ReservationState
    /// </summary>
    public static ReservationState Evolve(this ReservationState state, ReservationHoldCommitted committed) =>
        state switch
        {
            ReservationState.ReservationDraft draft => new ReservationState.ReservationHeld(
                draft.ReservationId,
                draft.RoomId,
                draft.OrganizerId,
                draft.StartTime,
                draft.EndTime,
                draft.Purpose,
                committed.RequiresApproval,
                committed.ApprovalRequestId),
            _ => state // Idempotency: ignore if already committed
        };
}
