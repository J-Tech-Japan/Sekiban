using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.EquipmentItem;

/// <summary>
///     EquipmentItem state using discriminated union pattern.
///     Tracks individual equipment items through their lifecycle.
/// </summary>
public abstract record EquipmentItemState : ITagStatePayload
{
    public static EquipmentItemState Empty => new EquipmentItemEmpty();

    /// <summary>
    ///     Empty/initial state before equipment item is registered
    /// </summary>
    public record EquipmentItemEmpty() : EquipmentItemState;

    /// <summary>
    ///     Available state - equipment item is available for checkout
    /// </summary>
    public record EquipmentItemAvailable(
        Guid EquipmentItemId,
        Guid EquipmentTypeId,
        string SerialNumber,
        string? Notes,
        DateTime RegisteredAt) : EquipmentItemState;

    /// <summary>
    ///     CheckedOut state - equipment item is currently checked out
    /// </summary>
    public record EquipmentItemCheckedOut(
        Guid EquipmentItemId,
        Guid EquipmentTypeId,
        string SerialNumber,
        string? Notes,
        DateTime RegisteredAt,
        Guid EquipmentReservationId,
        Guid CheckedOutBy,
        DateTime CheckedOutAt) : EquipmentItemState;

    /// <summary>
    ///     Retired state - equipment item is no longer in service
    /// </summary>
    public record EquipmentItemRetired(
        Guid EquipmentItemId,
        Guid EquipmentTypeId,
        string SerialNumber,
        string Reason,
        DateTime RetiredAt) : EquipmentItemState;
}
