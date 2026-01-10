using Dcb.MeetingRoomModels.Events.EquipmentReservation;
namespace Dcb.MeetingRoomModels.States.EquipmentItem.Deciders;

/// <summary>
///     Decider for EquipmentCheckedOut event (affects EquipmentItem state)
/// </summary>
public static class EquipmentItemCheckedOutDecider
{
    /// <summary>
    ///     Validate preconditions for checking out equipment item
    /// </summary>
    public static void Validate(this EquipmentItemState.EquipmentItemAvailable state)
    {
        // Item is available, can be checked out
    }

    /// <summary>
    ///     Apply EquipmentCheckedOut event to EquipmentItemState
    /// </summary>
    public static EquipmentItemState Evolve(this EquipmentItemState state, EquipmentCheckedOut checkedOut) =>
        state switch
        {
            EquipmentItemState.EquipmentItemAvailable available => new EquipmentItemState.EquipmentItemCheckedOut(
                available.EquipmentItemId,
                available.EquipmentTypeId,
                available.SerialNumber,
                available.Notes,
                available.RegisteredAt,
                checkedOut.EquipmentReservationId,
                checkedOut.CheckedOutBy,
                checkedOut.CheckedOutAt),
            _ => state // Idempotency: ignore if not available
        };
}
