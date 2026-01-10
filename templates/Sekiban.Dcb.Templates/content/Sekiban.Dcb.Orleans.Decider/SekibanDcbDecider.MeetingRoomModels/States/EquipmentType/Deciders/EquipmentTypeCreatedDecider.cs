using Dcb.MeetingRoomModels.Events.EquipmentType;
namespace Dcb.MeetingRoomModels.States.EquipmentType.Deciders;

/// <summary>
///     Decider for EquipmentTypeCreated event
/// </summary>
public static class EquipmentTypeCreatedDecider
{
    /// <summary>
    ///     Create a new EquipmentTypeActive from EquipmentTypeCreated event
    /// </summary>
    public static EquipmentTypeState.EquipmentTypeActive Create(EquipmentTypeCreated created) =>
        new(
            created.EquipmentTypeId,
            created.Name,
            created.Description,
            created.TotalQuantity,
            created.MaxPerReservation);

    /// <summary>
    ///     Apply EquipmentTypeCreated event to EquipmentTypeState
    /// </summary>
    public static EquipmentTypeState Evolve(this EquipmentTypeState state, EquipmentTypeCreated created) =>
        state switch
        {
            EquipmentTypeState.EquipmentTypeEmpty => Create(created),
            _ => state // Idempotency: ignore if already created
        };
}
