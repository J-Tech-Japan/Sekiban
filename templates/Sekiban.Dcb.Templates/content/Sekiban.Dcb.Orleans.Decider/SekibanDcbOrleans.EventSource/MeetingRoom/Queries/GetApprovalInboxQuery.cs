using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.Tags;
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
    List<Guid> ApproverIds,
    DateTime RequestedAt,
    string Status);

[GenerateSerializer]
public record GetApprovalInboxQuery :
    IMultiProjectionListQuery<GenericTagMultiProjector<ApprovalRequestProjector, ApprovalRequestTag>, GetApprovalInboxQuery, ApprovalInboxItem>,
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
        GenericTagMultiProjector<ApprovalRequestProjector, ApprovalRequestTag> projector,
        GetApprovalInboxQuery query,
        IQueryContext context)
    {
        var states = projector.GetStatePayloads().OfType<ApprovalRequestState>();

        return states.Select(s => s switch
        {
            ApprovalRequestState.ApprovalRequestPending pending => new ApprovalInboxItem(
                pending.ApprovalRequestId,
                pending.ReservationId,
                pending.RoomId,
                pending.RequesterId,
                pending.ApproverIds,
                pending.RequestedAt,
                "Pending"),
            ApprovalRequestState.ApprovalRequestApproved approved => new ApprovalInboxItem(
                approved.ApprovalRequestId,
                approved.ReservationId,
                Guid.Empty,
                Guid.Empty,
                [],
                approved.DecidedAt,
                "Approved"),
            ApprovalRequestState.ApprovalRequestRejected rejected => new ApprovalInboxItem(
                rejected.ApprovalRequestId,
                rejected.ReservationId,
                Guid.Empty,
                Guid.Empty,
                [],
                rejected.DecidedAt,
                "Rejected"),
            _ => null
        })
        .Where(x => x != null)
        .Cast<ApprovalInboxItem>()
        .Where(x => !query.PendingOnly || x.Status == "Pending");
    }

    public static IEnumerable<ApprovalInboxItem> HandleSort(
        IEnumerable<ApprovalInboxItem> filteredList,
        GetApprovalInboxQuery query,
        IQueryContext context) =>
        filteredList.OrderByDescending(s => s.RequestedAt);
}
