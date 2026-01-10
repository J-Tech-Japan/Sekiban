using Dcb.MeetingRoomModels.Events.EquipmentType;
namespace Dcb.MeetingRoomModels.States.EquipmentType.Deciders;

/// <summary>
///     Decider for EquipmentTypeUpdated event
/// </summary>
public static class EquipmentTypeUpdatedDecider
{
    /// <summary>
    ///     Apply EquipmentTypeUpdated event to EquipmentTypeState
    /// </summary>
    public static EquipmentTypeState Evolve(this EquipmentTypeState state, EquipmentTypeUpdated updated) =>
        state switch
        {
            EquipmentTypeState.EquipmentTypeActive active => active with
            {
                Name = updated.Name,
                Description = updated.Description,
                MaxPerReservation = updated.MaxPerReservation
            },
            _ => state // Idempotency: ignore if not active
        };
}
