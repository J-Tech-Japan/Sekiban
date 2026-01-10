using Dcb.MeetingRoomModels.Events.EquipmentItem;
namespace Dcb.MeetingRoomModels.States.EquipmentItem.Deciders;

/// <summary>
///     Decider for EquipmentItemRetired event
/// </summary>
public static class EquipmentItemRetiredDecider
{
    /// <summary>
    ///     Validate preconditions for retiring equipment item
    /// </summary>
    public static void Validate(this EquipmentItemState.EquipmentItemAvailable state)
    {
        // Item is available, can be retired
    }

    /// <summary>
    ///     Apply EquipmentItemRetired event to EquipmentItemState
    /// </summary>
    public static EquipmentItemState Evolve(this EquipmentItemState state, EquipmentItemRetired retired) =>
        state switch
        {
            EquipmentItemState.EquipmentItemAvailable available => new EquipmentItemState.EquipmentItemRetired(
                available.EquipmentItemId,
                available.EquipmentTypeId,
                available.SerialNumber,
                retired.Reason,
                retired.RetiredAt),
            _ => state // Idempotency: cannot retire if checked out or already retired
        };
}
