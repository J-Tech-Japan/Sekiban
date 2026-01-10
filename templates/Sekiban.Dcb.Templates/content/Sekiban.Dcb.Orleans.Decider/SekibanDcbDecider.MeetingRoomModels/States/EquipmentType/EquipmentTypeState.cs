using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.EquipmentType;

/// <summary>
///     EquipmentType state using discriminated union pattern.
/// </summary>
public abstract record EquipmentTypeState : ITagStatePayload
{
    public static EquipmentTypeState Empty => new EquipmentTypeEmpty();

    /// <summary>
    ///     Empty/initial state before equipment type is created
    /// </summary>
    public record EquipmentTypeEmpty() : EquipmentTypeState;

    /// <summary>
    ///     Active state - equipment type is available for use
    /// </summary>
    public record EquipmentTypeActive(
        Guid EquipmentTypeId,
        string Name,
        string Description,
        int TotalQuantity,
        int MaxPerReservation) : EquipmentTypeState;
}
