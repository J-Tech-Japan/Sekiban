using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.MeetingRoom.Queries;

public record ApprovalInboxItem(
    Guid ApprovalRequestId,
    Guid ReservationId,
    Guid RoomId,
    Guid RequesterId,
    string? RequestComment,
    List<Guid> ApproverIds,
    DateTime RequestedAt,
    string Status);

public record GetApprovalInboxQuery :
    IMultiProjectionListQuery<ApprovalRequestListProjection, GetApprovalInboxQuery, ApprovalInboxItem>,
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
    public bool PendingOnly { get; init; } = true;

    public static IEnumerable<ApprovalInboxItem> HandleFilter(
        ApprovalRequestListProjection projector,
        GetApprovalInboxQuery query,
        IQueryContext context)
    {
        var states = query.PendingOnly
            ? projector.GetPendingApprovalRequests().Cast<ApprovalRequestState>()
            : projector.GetAllApprovalRequests().Cast<ApprovalRequestState>();

        return states.Select(s => s switch
        {
            ApprovalRequestState.ApprovalRequestPending pending => new ApprovalInboxItem(
                pending.ApprovalRequestId,
                pending.ReservationId,
                pending.RoomId,
                pending.RequesterId,
                pending.RequestComment,
                pending.ApproverIds,
                pending.RequestedAt,
                "Pending"),
            ApprovalRequestState.ApprovalRequestApproved approved => new ApprovalInboxItem(
                approved.ApprovalRequestId,
                approved.ReservationId,
                approved.RoomId,
                approved.RequesterId,
                approved.RequestComment,
                approved.ApproverIds,
                approved.DecidedAt,
                "Approved"),
            ApprovalRequestState.ApprovalRequestRejected rejected => new ApprovalInboxItem(
                rejected.ApprovalRequestId,
                rejected.ReservationId,
                rejected.RoomId,
                rejected.RequesterId,
                rejected.RequestComment,
                rejected.ApproverIds,
                rejected.DecidedAt,
                "Rejected"),
            _ => null
        })
        .Where(x => x != null)
        .Cast<ApprovalInboxItem>();
    }

    public static IEnumerable<ApprovalInboxItem> HandleSort(
        IEnumerable<ApprovalInboxItem> filteredList,
        GetApprovalInboxQuery query,
        IQueryContext context) =>
        filteredList.OrderByDescending(s => s.RequestedAt);
}
