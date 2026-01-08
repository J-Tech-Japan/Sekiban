using Dcb.MeetingRoomModels.Events.EquipmentReservation;
namespace Dcb.MeetingRoomModels.States.EquipmentReservation.Deciders;

/// <summary>
///     Decider for EquipmentItemAssigned event
/// </summary>
public static class EquipmentItemAssignedDecider
{
    /// <summary>
    ///     Validate preconditions for assigning item
    /// </summary>
    public static void Validate(this EquipmentReservationState.EquipmentReservationPending state, Guid itemId)
    {
        // Pending state can accept item assignments
    }

    /// <summary>
    ///     Validate preconditions for assigning item to already assigned state
    /// </summary>
    public static void Validate(this EquipmentReservationState.EquipmentReservationAssigned state, Guid itemId)
    {
        if (state.AssignedItemIds.Contains(itemId))
        {
            throw new InvalidOperationException($"Item {itemId} is already assigned to this reservation");
        }
        if (state.AssignedItemIds.Count >= state.Quantity)
        {
            throw new InvalidOperationException($"Reservation already has {state.Quantity} items assigned");
        }
    }

    /// <summary>
    ///     Apply EquipmentItemAssigned event to EquipmentReservationState
    /// </summary>
    public static EquipmentReservationState Evolve(this EquipmentReservationState state, EquipmentItemAssigned assigned) =>
        state switch
        {
            EquipmentReservationState.EquipmentReservationPending pending =>
                new EquipmentReservationState.EquipmentReservationAssigned(
                    pending.EquipmentReservationId,
                    pending.EquipmentTypeId,
                    pending.Quantity,
                    pending.RoomReservationId,
                    pending.RequesterId,
                    pending.StartTime,
                    pending.EndTime,
                    [assigned.EquipmentItemId]),
            EquipmentReservationState.EquipmentReservationAssigned existing =>
                existing.AssignedItemIds.Contains(assigned.EquipmentItemId)
                    ? existing // Idempotency
                    : existing with { AssignedItemIds = [..existing.AssignedItemIds, assigned.EquipmentItemId] },
            _ => state // Idempotency: ignore if not in valid state
        };
}
