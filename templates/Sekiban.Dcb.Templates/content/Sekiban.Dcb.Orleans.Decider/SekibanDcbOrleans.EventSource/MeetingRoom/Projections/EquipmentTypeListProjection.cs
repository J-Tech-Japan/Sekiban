using Dcb.MeetingRoomModels.Events.EquipmentType;
using Dcb.MeetingRoomModels.States.EquipmentType;
using Dcb.MeetingRoomModels.States.EquipmentType.Deciders;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Projections;

/// <summary>
///     EquipmentType list projection for multi-projection queries
/// </summary>
public record EquipmentTypeListProjection : IMultiProjector<EquipmentTypeListProjection>
{
    public Dictionary<Guid, EquipmentTypeState> EquipmentTypes { get; init; } = [];

    public static string MultiProjectorName => nameof(EquipmentTypeListProjection);
    public static string MultiProjectorVersion => "1.0.0";

    public static EquipmentTypeListProjection GenerateInitialPayload() => new();

    public static EquipmentTypeListProjection Project(
        EquipmentTypeListProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var equipmentTypeTags = tags.OfType<EquipmentTypeTag>().ToList();
        if (equipmentTypeTags.Count == 0) return payload;

        var updatedEquipmentTypes = new Dictionary<Guid, EquipmentTypeState>(payload.EquipmentTypes);

        foreach (var tag in equipmentTypeTags)
        {
            var equipmentTypeId = tag.EquipmentTypeId;
            var currentState = updatedEquipmentTypes.TryGetValue(equipmentTypeId, out var existing)
                ? existing
                : EquipmentTypeState.Empty;

            var newState = ev.Payload switch
            {
                EquipmentTypeCreated created => currentState.Evolve(created),
                EquipmentTypeUpdated updated => currentState.Evolve(updated),
                _ => currentState
            };

            if (newState is not EquipmentTypeState.EquipmentTypeEmpty)
            {
                updatedEquipmentTypes[equipmentTypeId] = newState;
            }
        }

        return payload with { EquipmentTypes = updatedEquipmentTypes };
    }

    /// <summary>
    ///     Get all active equipment types
    /// </summary>
    public IReadOnlyList<EquipmentTypeState.EquipmentTypeActive> GetActiveEquipmentTypes() =>
        [.. EquipmentTypes.Values.OfType<EquipmentTypeState.EquipmentTypeActive>()
            .OrderBy(e => e.Name, StringComparer.Ordinal)];

    /// <summary>
    ///     Get all equipment types
    /// </summary>
    public IReadOnlyList<EquipmentTypeState> GetAllEquipmentTypes() =>
        [.. EquipmentTypes.Values
            .Where(e => e is not EquipmentTypeState.EquipmentTypeEmpty)];

    /// <summary>
    ///     Get equipment type by ID
    /// </summary>
    public EquipmentTypeState? GetEquipmentType(Guid equipmentTypeId) =>
        EquipmentTypes.TryGetValue(equipmentTypeId, out var equipmentType) ? equipmentType : null;

    /// <summary>
    ///     Get equipment types with available quantity
    /// </summary>
    public IReadOnlyList<EquipmentTypeState.EquipmentTypeActive> GetEquipmentTypesWithAvailability(int minQuantity) =>
        [.. EquipmentTypes.Values.OfType<EquipmentTypeState.EquipmentTypeActive>()
            .Where(e => e.TotalQuantity >= minQuantity)
            .OrderBy(e => e.Name, StringComparer.Ordinal)];
}
