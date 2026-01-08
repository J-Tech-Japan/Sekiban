using Dcb.MeetingRoomModels.Events.Reservation;
namespace Dcb.MeetingRoomModels.States.Reservation.Deciders;

/// <summary>
///     Decider for ReservationRejected event
/// </summary>
public static class ReservationRejectedDecider
{
    /// <summary>
    ///     Apply ReservationRejected event to ReservationState
    /// </summary>
    public static ReservationState Evolve(this ReservationState state, ReservationRejected rejected) =>
        state switch
        {
            ReservationState.ReservationHeld held when held.RequiresApproval =>
                new ReservationState.ReservationRejected(
                    held.ReservationId,
                    held.RoomId,
                    held.OrganizerId,
                    held.OrganizerName,
                    held.StartTime,
                    held.EndTime,
                    held.Purpose,
                    rejected.ApprovalRequestId,
                    held.ApprovalRequestComment,
                    rejected.Reason,
                    rejected.RejectedAt),
            _ => state // Idempotency: ignore if not in held state requiring approval
        };
}
