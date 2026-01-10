using Dcb.MeetingRoomModels.Events.EquipmentReservation;
namespace Dcb.MeetingRoomModels.States.EquipmentReservation.Deciders;

/// <summary>
///     Decider for EquipmentReservationCancelled event
/// </summary>
public static class EquipmentReservationCancelledDecider
{
    /// <summary>
    ///     Validate preconditions for cancelling reservation
    /// </summary>
    public static void Validate(this EquipmentReservationState.EquipmentReservationCheckedOut state)
    {
        throw new InvalidOperationException("Cannot cancel reservation with checked out equipment. Return items first.");
    }

    /// <summary>
    ///     Apply EquipmentReservationCancelled event to EquipmentReservationState
    /// </summary>
    public static EquipmentReservationState Evolve(this EquipmentReservationState state, EquipmentReservationCancelled cancelled) =>
        state switch
        {
            EquipmentReservationState.EquipmentReservationPending => new EquipmentReservationState.EquipmentReservationCancelled(
                cancelled.EquipmentReservationId,
                cancelled.Reason,
                cancelled.CancelledAt),
            EquipmentReservationState.EquipmentReservationAssigned => new EquipmentReservationState.EquipmentReservationCancelled(
                cancelled.EquipmentReservationId,
                cancelled.Reason,
                cancelled.CancelledAt),
            _ => state // Idempotency: cannot cancel if checked out, returned, or already cancelled
        };
}
