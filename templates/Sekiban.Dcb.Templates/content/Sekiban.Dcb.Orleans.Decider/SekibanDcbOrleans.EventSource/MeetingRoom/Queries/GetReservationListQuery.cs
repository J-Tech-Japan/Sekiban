using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.EventSource.MeetingRoom.Projections;
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
    List<string> SelectedEquipment,
    string Status,
    bool RequiresApproval,
    Guid? ApprovalRequestId,
    string? ApprovalRequestComment,
    string? ApprovalDecisionComment);

[GenerateSerializer]
public record GetReservationListQuery :
    IMultiProjectionListQuery<ReservationListProjection, GetReservationListQuery, ReservationListItem>,
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
        ReservationListProjection projector,
        GetReservationListQuery query,
        IQueryContext context)
    {
        var items = projector.GetAllReservations()
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
                    d.SelectedEquipment ?? [],
                    "Draft",
                    false,
                    null,
                    null,
                    null),
                ReservationState.ReservationHeld h => new ReservationListItem(
                    h.ReservationId,
                    h.RoomId,
                    h.OrganizerId,
                    h.OrganizerName,
                    h.StartTime,
                    h.EndTime,
                    h.Purpose,
                    h.SelectedEquipment ?? [],
                    "Held",
                    h.RequiresApproval,
                    h.ApprovalRequestId,
                    h.ApprovalRequestComment,
                    null),
                ReservationState.ReservationConfirmed c => new ReservationListItem(
                    c.ReservationId,
                    c.RoomId,
                    c.OrganizerId,
                    c.OrganizerName,
                    c.StartTime,
                    c.EndTime,
                    c.Purpose,
                    c.SelectedEquipment ?? [],
                    "Confirmed",
                    c.ApprovalRequestId != null,
                    c.ApprovalRequestId,
                    c.ApprovalRequestComment,
                    c.ApprovalDecisionComment),
                ReservationState.ReservationCancelled cancelled => new ReservationListItem(
                    cancelled.ReservationId,
                    cancelled.RoomId,
                    cancelled.OrganizerId,
                    cancelled.OrganizerName,
                    cancelled.StartTime,
                    cancelled.EndTime,
                    cancelled.Purpose,
                    cancelled.SelectedEquipment ?? [],
                    "Cancelled",
                    false,
                    null,
                    cancelled.ApprovalRequestComment,
                    cancelled.Reason),
                ReservationState.ReservationRejected rejected => new ReservationListItem(
                    rejected.ReservationId,
                    rejected.RoomId,
                    rejected.OrganizerId,
                    rejected.OrganizerName,
                    rejected.StartTime,
                    rejected.EndTime,
                    rejected.Purpose,
                    rejected.SelectedEquipment ?? [],
                    "Rejected",
                    true,
                    rejected.ApprovalRequestId,
                    rejected.ApprovalRequestComment,
                    rejected.Reason),
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
