using Dcb.MeetingRoomModels.Events.EquipmentReservation;
namespace Dcb.MeetingRoomModels.States.EquipmentReservation.Deciders;

/// <summary>
///     Decider for EquipmentReturned event (affects EquipmentReservation state)
/// </summary>
public static class EquipmentReservationReturnedDecider
{
    /// <summary>
    ///     Apply EquipmentReturned event to EquipmentReservationState.
    ///     Note: This transitions to Returned state when all items are returned.
    /// </summary>
    public static EquipmentReservationState Evolve(
        this EquipmentReservationState state,
        EquipmentReturned returned,
        int totalItemsToReturn) =>
        state switch
        {
            EquipmentReservationState.EquipmentReservationCheckedOut checkedOut when totalItemsToReturn <= 1 =>
                new EquipmentReservationState.EquipmentReservationReturned(
                    checkedOut.EquipmentReservationId,
                    checkedOut.EquipmentTypeId,
                    checkedOut.RoomReservationId,
                    returned.ReturnedAt),
            _ => state // Partial return or invalid state
        };

    /// <summary>
    ///     Simplified evolve for single-item returns (assumes all items returned at once)
    /// </summary>
    public static EquipmentReservationState Evolve(this EquipmentReservationState state, EquipmentReturned returned) =>
        state switch
        {
            EquipmentReservationState.EquipmentReservationCheckedOut checkedOut =>
                new EquipmentReservationState.EquipmentReservationReturned(
                    checkedOut.EquipmentReservationId,
                    checkedOut.EquipmentTypeId,
                    checkedOut.RoomReservationId,
                    returned.ReturnedAt),
            _ => state // Idempotency: ignore if not checked out
        };
}
