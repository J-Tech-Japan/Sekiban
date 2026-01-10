using Dcb.MeetingRoomModels.Events.Reservation;
namespace Dcb.MeetingRoomModels.States.Reservation.Deciders;

/// <summary>
///     Decider for ReservationDetailsUpdated event.
///     Note: Current state doesn't store details like Title, Description, etc.
///     This decider is a placeholder for future enhancement.
/// </summary>
public static class ReservationDetailsUpdatedDecider
{
    /// <summary>
    ///     Apply ReservationDetailsUpdated event to ReservationState.
    ///     Currently returns the same state as details are not stored in state.
    /// </summary>
    public static ReservationState Evolve(this ReservationState state, ReservationDetailsUpdated updated) =>
        state; // Details update doesn't change core state currently
}
