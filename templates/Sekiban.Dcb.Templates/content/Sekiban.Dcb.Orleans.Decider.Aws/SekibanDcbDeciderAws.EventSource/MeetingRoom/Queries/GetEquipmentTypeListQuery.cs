using Dcb.MeetingRoomModels.States.EquipmentType;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.EventSource.MeetingRoom.Equipment;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.MeetingRoom.Queries;

public record EquipmentTypeListItem(
    Guid EquipmentTypeId,
    string Name,
    string Description,
    int TotalQuantity,
    int MaxPerReservation);

public record GetEquipmentTypeListQuery :
    IMultiProjectionListQuery<EquipmentTypeListProjection, GetEquipmentTypeListQuery, EquipmentTypeListItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    [Id(0)]
    public int? PageNumber { get; init; }

    [Id(1)]
    public int? PageSize { get; init; }

    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }

    public static IEnumerable<EquipmentTypeListItem> HandleFilter(
        EquipmentTypeListProjection projector,
        GetEquipmentTypeListQuery query,
        IQueryContext context)
    {
        return projector.GetActiveEquipmentTypes()
            .Select(s => new EquipmentTypeListItem(
                s.EquipmentTypeId,
                s.Name,
                s.Description,
                s.TotalQuantity,
                s.MaxPerReservation));
    }

    public static IEnumerable<EquipmentTypeListItem> HandleSort(
        IEnumerable<EquipmentTypeListItem> filteredList,
        GetEquipmentTypeListQuery query,
        IQueryContext context) =>
        filteredList.OrderBy(s => s.Name, StringComparer.Ordinal);
}
