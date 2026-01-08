using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Dcb.EventSource.MeetingRoom.Reservation;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.MeetingRoom.Queries;

public record ReservationListItem(
    Guid ReservationId,
    Guid RoomId,
    Guid OrganizerId,
    string OrganizerName,
    DateTime StartTime,
    DateTime EndTime,
    string Purpose,
    string Status,
    bool RequiresApproval,
    Guid? ApprovalRequestId);

[GenerateSerializer]
public record GetReservationListQuery :
    IMultiProjectionListQuery<GenericTagMultiProjector<ReservationProjector, ReservationTag>, GetReservationListQuery, ReservationListItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    [Id(0)]
    public int? PageNumber { get; init; }

    [Id(1)]
    public int? PageSize { get; init; }

    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }

    [Id(3)]
    public Guid? RoomId { get; init; }

    public static IEnumerable<ReservationListItem> HandleFilter(
        GenericTagMultiProjector<ReservationProjector, ReservationTag> projector,
        GetReservationListQuery query,
        IQueryContext context)
    {
        var items = projector.GetStatePayloads()
            .OfType<ReservationState>()
            .Where(s => s is not ReservationState.ReservationEmpty)
            .Select(s => s switch
            {
                ReservationState.ReservationDraft d => new ReservationListItem(
                    d.ReservationId,
                    d.RoomId,
                    d.OrganizerId,
                    d.OrganizerName,
                    d.StartTime,
                    d.EndTime,
                    d.Purpose,
                    "Draft",
                    false,
                    null),
                ReservationState.ReservationHeld h => new ReservationListItem(
                    h.ReservationId,
                    h.RoomId,
                    h.OrganizerId,
                    h.OrganizerName,
                    h.StartTime,
                    h.EndTime,
                    h.Purpose,
                    "Held",
                    h.RequiresApproval,
                    h.ApprovalRequestId),
                ReservationState.ReservationConfirmed c => new ReservationListItem(
                    c.ReservationId,
                    c.RoomId,
                    c.OrganizerId,
                    c.OrganizerName,
                    c.StartTime,
                    c.EndTime,
                    c.Purpose,
                    "Confirmed",
                    false,
                    null),
                ReservationState.ReservationCancelled cancelled => new ReservationListItem(
                    cancelled.ReservationId,
                    cancelled.RoomId,
                    Guid.Empty,
                    "",
                    DateTime.MinValue,
                    DateTime.MinValue,
                    "",
                    "Cancelled",
                    false,
                    null),
                ReservationState.ReservationRejected rejected => new ReservationListItem(
                    rejected.ReservationId,
                    rejected.RoomId,
                    Guid.Empty,
                    "",
                    DateTime.MinValue,
                    DateTime.MinValue,
                    "",
                    "Rejected",
                    false,
                    null),
                _ => null!
            })
            .Where(item => item != null && item.ReservationId != Guid.Empty);

        if (query.RoomId.HasValue)
        {
            items = items.Where(item => item.RoomId == query.RoomId.Value);
        }

        return items;
    }

    public static IEnumerable<ReservationListItem> HandleSort(
        IEnumerable<ReservationListItem> filteredList,
        GetReservationListQuery query,
        IQueryContext context) =>
        filteredList.OrderBy(s => s.StartTime);
}
