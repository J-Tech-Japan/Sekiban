using Dcb.MeetingRoomModels.Events.EquipmentReservation;
namespace Dcb.MeetingRoomModels.States.EquipmentReservation.Deciders;

/// <summary>
///     Decider for EquipmentCheckedOut event (affects EquipmentReservation state)
/// </summary>
public static class EquipmentReservationCheckedOutDecider
{
    /// <summary>
    ///     Validate preconditions for checking out
    /// </summary>
    public static void Validate(this EquipmentReservationState.EquipmentReservationAssigned state)
    {
        if (state.AssignedItemIds.Count == 0)
        {
            throw new InvalidOperationException("No items assigned to checkout");
        }
    }

    /// <summary>
    ///     Apply EquipmentCheckedOut event to EquipmentReservationState
    /// </summary>
    public static EquipmentReservationState Evolve(this EquipmentReservationState state, EquipmentCheckedOut checkedOut) =>
        state switch
        {
            EquipmentReservationState.EquipmentReservationAssigned assigned =>
                new EquipmentReservationState.EquipmentReservationCheckedOut(
                    assigned.EquipmentReservationId,
                    assigned.EquipmentTypeId,
                    assigned.RoomReservationId,
                    assigned.RequesterId,
                    assigned.StartTime,
                    assigned.EndTime,
                    assigned.AssignedItemIds,
                    checkedOut.CheckedOutBy,
                    checkedOut.CheckedOutAt),
            EquipmentReservationState.EquipmentReservationCheckedOut existing =>
                existing, // Idempotency: already checked out
            _ => state // Idempotency: ignore if not in valid state
        };
}
