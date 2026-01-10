using Dcb.MeetingRoomModels.Events.Reservation;
namespace Dcb.MeetingRoomModels.States.Reservation.Deciders;

/// <summary>
///     Decider for ReservationConfirmed event
/// </summary>
public static class ReservationConfirmedDecider
{
    /// <summary>
    ///     Validate preconditions for confirming reservation
    /// </summary>
    public static void Validate(this ReservationState.ReservationHeld state)
    {
        if (state.RequiresApproval && state.ApprovalRequestId == null)
        {
            throw new InvalidOperationException("Cannot confirm reservation that requires approval without approval request");
        }
    }

    /// <summary>
    ///     Apply ReservationConfirmed event to ReservationState
    /// </summary>
    public static ReservationState Evolve(this ReservationState state, ReservationConfirmed confirmed) =>
        state switch
        {
            ReservationState.ReservationHeld held => new ReservationState.ReservationConfirmed(
                held.ReservationId,
                held.RoomId,
                held.OrganizerId,
                held.OrganizerName,
                held.StartTime,
                held.EndTime,
                held.Purpose,
                held.SelectedEquipment,
                confirmed.ConfirmedAt,
                held.ApprovalRequestId,
                held.ApprovalRequestComment,
                confirmed.ApprovalDecisionComment),
            _ => state // Idempotency: ignore if already confirmed
        };
}
