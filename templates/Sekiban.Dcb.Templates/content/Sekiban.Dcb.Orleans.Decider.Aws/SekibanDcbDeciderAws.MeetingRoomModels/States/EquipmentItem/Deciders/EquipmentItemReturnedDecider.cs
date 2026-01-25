using Dcb.MeetingRoomModels.Events.EquipmentReservation;
namespace Dcb.MeetingRoomModels.States.EquipmentItem.Deciders;

/// <summary>
///     Decider for EquipmentReturned event (affects EquipmentItem state)
/// </summary>
public static class EquipmentItemReturnedDecider
{
    /// <summary>
    ///     Validate preconditions for returning equipment item
    /// </summary>
    public static void Validate(this EquipmentItemState.EquipmentItemCheckedOut state, Guid reservationId)
    {
        if (state.EquipmentReservationId != reservationId)
        {
            throw new InvalidOperationException(
                $"Equipment item {state.EquipmentItemId} is checked out to reservation {state.EquipmentReservationId}, not {reservationId}");
        }
    }

    /// <summary>
    ///     Apply EquipmentReturned event to EquipmentItemState
    /// </summary>
    public static EquipmentItemState Evolve(this EquipmentItemState state, EquipmentReturned returned) =>
        state switch
        {
            EquipmentItemState.EquipmentItemCheckedOut checkedOut => new EquipmentItemState.EquipmentItemAvailable(
                checkedOut.EquipmentItemId,
                checkedOut.EquipmentTypeId,
                checkedOut.SerialNumber,
                checkedOut.Notes,
                checkedOut.RegisteredAt),
            _ => state // Idempotency: ignore if not checked out
        };
}
