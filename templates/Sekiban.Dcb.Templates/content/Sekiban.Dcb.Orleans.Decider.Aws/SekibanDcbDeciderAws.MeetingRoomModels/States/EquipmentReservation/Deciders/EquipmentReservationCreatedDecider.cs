using Dcb.MeetingRoomModels.Events.EquipmentReservation;
namespace Dcb.MeetingRoomModels.States.EquipmentReservation.Deciders;

/// <summary>
///     Decider for EquipmentReservationCreated event
/// </summary>
public static class EquipmentReservationCreatedDecider
{
    /// <summary>
    ///     Create a new EquipmentReservationPending from EquipmentReservationCreated event
    /// </summary>
    public static EquipmentReservationState.EquipmentReservationPending Create(EquipmentReservationCreated created) =>
        new(
            created.EquipmentReservationId,
            created.EquipmentTypeId,
            created.Quantity,
            created.RoomReservationId,
            created.RequesterId,
            created.StartTime,
            created.EndTime);

    /// <summary>
    ///     Apply EquipmentReservationCreated event to EquipmentReservationState
    /// </summary>
    public static EquipmentReservationState Evolve(this EquipmentReservationState state, EquipmentReservationCreated created) =>
        state switch
        {
            EquipmentReservationState.EquipmentReservationEmpty => Create(created),
            _ => state // Idempotency: ignore if already created
        };
}
