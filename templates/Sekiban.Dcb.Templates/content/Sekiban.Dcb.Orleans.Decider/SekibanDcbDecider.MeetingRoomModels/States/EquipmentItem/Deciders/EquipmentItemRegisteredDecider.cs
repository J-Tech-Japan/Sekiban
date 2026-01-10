using Dcb.MeetingRoomModels.Events.EquipmentItem;
namespace Dcb.MeetingRoomModels.States.EquipmentItem.Deciders;

/// <summary>
///     Decider for EquipmentItemRegistered event
/// </summary>
public static class EquipmentItemRegisteredDecider
{
    /// <summary>
    ///     Create a new EquipmentItemAvailable from EquipmentItemRegistered event
    /// </summary>
    public static EquipmentItemState.EquipmentItemAvailable Create(EquipmentItemRegistered registered) =>
        new(
            registered.EquipmentItemId,
            registered.EquipmentTypeId,
            registered.SerialNumber,
            registered.Notes,
            registered.RegisteredAt);

    /// <summary>
    ///     Apply EquipmentItemRegistered event to EquipmentItemState
    /// </summary>
    public static EquipmentItemState Evolve(this EquipmentItemState state, EquipmentItemRegistered registered) =>
        state switch
        {
            EquipmentItemState.EquipmentItemEmpty => Create(registered),
            _ => state // Idempotency: ignore if already registered
        };
}
