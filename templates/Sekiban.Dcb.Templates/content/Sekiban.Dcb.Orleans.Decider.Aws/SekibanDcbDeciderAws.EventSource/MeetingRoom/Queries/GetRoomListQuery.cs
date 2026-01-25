using Dcb.MeetingRoomModels.States.Room;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.EventSource.MeetingRoom.Room;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.MeetingRoom.Queries;

public record RoomListItem(
    Guid RoomId,
    string Name,
    int Capacity,
    string Location,
    List<string> Equipment,
    bool RequiresApproval,
    bool IsActive);

[GenerateSerializer]
public record GetRoomListQuery :
    IMultiProjectionListQuery<RoomListProjection, GetRoomListQuery, RoomListItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    [Id(0)]
    public int? PageNumber { get; init; }

    [Id(1)]
    public int? PageSize { get; init; }

    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }

    public static IEnumerable<RoomListItem> HandleFilter(
        RoomListProjection projector,
        GetRoomListQuery query,
        IQueryContext context)
    {
        return projector.GetAllRooms()
            .Select(s => new RoomListItem(
                s.RoomId,
                s.Name,
                s.Capacity,
                s.Location,
                s.Equipment,
                s.RequiresApproval,
                s.IsActive));
    }

    public static IEnumerable<RoomListItem> HandleSort(
        IEnumerable<RoomListItem> filteredList,
        GetRoomListQuery query,
        IQueryContext context) =>
        filteredList.OrderBy(s => s.Name, StringComparer.Ordinal);
}
