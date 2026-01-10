using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.EquipmentReservation;

/// <summary>
///     EquipmentReservation state using discriminated union pattern.
///     Tracks equipment reservation through its lifecycle.
/// </summary>
public abstract record EquipmentReservationState : ITagStatePayload
{
    public static EquipmentReservationState Empty => new EquipmentReservationEmpty();

    /// <summary>
    ///     Empty/initial state before equipment reservation is created
    /// </summary>
    public record EquipmentReservationEmpty() : EquipmentReservationState;

    /// <summary>
    ///     Pending state - reservation created, awaiting item assignment
    /// </summary>
    public record EquipmentReservationPending(
        Guid EquipmentReservationId,
        Guid EquipmentTypeId,
        int Quantity,
        Guid? RoomReservationId,
        Guid RequesterId,
        DateTime StartTime,
        DateTime EndTime) : EquipmentReservationState;

    /// <summary>
    ///     Assigned state - specific items have been assigned to the reservation
    /// </summary>
    public record EquipmentReservationAssigned(
        Guid EquipmentReservationId,
        Guid EquipmentTypeId,
        int Quantity,
        Guid? RoomReservationId,
        Guid RequesterId,
        DateTime StartTime,
        DateTime EndTime,
        List<Guid> AssignedItemIds) : EquipmentReservationState;

    /// <summary>
    ///     CheckedOut state - equipment has been picked up
    /// </summary>
    public record EquipmentReservationCheckedOut(
        Guid EquipmentReservationId,
        Guid EquipmentTypeId,
        Guid? RoomReservationId,
        Guid RequesterId,
        DateTime StartTime,
        DateTime EndTime,
        List<Guid> CheckedOutItemIds,
        Guid CheckedOutBy,
        DateTime CheckedOutAt) : EquipmentReservationState;

    /// <summary>
    ///     Returned state - all equipment has been returned
    /// </summary>
    public record EquipmentReservationReturned(
        Guid EquipmentReservationId,
        Guid EquipmentTypeId,
        Guid? RoomReservationId,
        DateTime ReturnedAt) : EquipmentReservationState;

    /// <summary>
    ///     Cancelled state - reservation was cancelled
    /// </summary>
    public record EquipmentReservationCancelled(
        Guid EquipmentReservationId,
        string Reason,
        DateTime CancelledAt) : EquipmentReservationState;
}
