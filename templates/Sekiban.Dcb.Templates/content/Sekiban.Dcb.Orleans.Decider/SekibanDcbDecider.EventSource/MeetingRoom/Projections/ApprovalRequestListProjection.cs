using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest.Deciders;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Projections;

/// <summary>
///     ApprovalRequest list projection for multi-projection queries
/// </summary>
public record ApprovalRequestListProjection : IMultiProjector<ApprovalRequestListProjection>
{
    public Dictionary<Guid, ApprovalRequestState> ApprovalRequests { get; init; } = [];

    public static string MultiProjectorName => nameof(ApprovalRequestListProjection);
    public static string MultiProjectorVersion => "1.0.0";

    public static ApprovalRequestListProjection GenerateInitialPayload() => new();

    public static ApprovalRequestListProjection Project(
        ApprovalRequestListProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var approvalRequestTags = tags.OfType<ApprovalRequestTag>().ToList();
        if (approvalRequestTags.Count == 0) return payload;

        var updatedApprovalRequests = new Dictionary<Guid, ApprovalRequestState>(payload.ApprovalRequests);

        foreach (var tag in approvalRequestTags)
        {
            var approvalRequestId = tag.ApprovalRequestId;
            var currentState = updatedApprovalRequests.TryGetValue(approvalRequestId, out var existing)
                ? existing
                : ApprovalRequestState.Empty;

            var newState = ev.Payload switch
            {
                ApprovalFlowStarted started => currentState.Evolve(started),
                ApprovalDecisionRecorded decision => currentState.Evolve(decision),
                _ => currentState
            };

            if (newState is not ApprovalRequestState.ApprovalRequestEmpty)
            {
                updatedApprovalRequests[approvalRequestId] = newState;
            }
        }

        return payload with { ApprovalRequests = updatedApprovalRequests };
    }

    /// <summary>
    ///     Get all pending approval requests
    /// </summary>
    public IReadOnlyList<ApprovalRequestState.ApprovalRequestPending> GetPendingApprovalRequests() =>
        [.. ApprovalRequests.Values.OfType<ApprovalRequestState.ApprovalRequestPending>()
            .OrderBy(r => r.RequestedAt)];

    /// <summary>
    ///     Get pending approval requests for a specific approver
    /// </summary>
    public IReadOnlyList<ApprovalRequestState.ApprovalRequestPending> GetPendingApprovalRequestsForApprover(Guid approverId) =>
        [.. ApprovalRequests.Values.OfType<ApprovalRequestState.ApprovalRequestPending>()
            .Where(r => r.ApproverIds.Contains(approverId))
            .OrderBy(r => r.RequestedAt)];

    /// <summary>
    ///     Get all approval requests
    /// </summary>
    public IReadOnlyList<ApprovalRequestState> GetAllApprovalRequests() =>
        [.. ApprovalRequests.Values];

    /// <summary>
    ///     Get approval request by ID
    /// </summary>
    public ApprovalRequestState? GetApprovalRequest(Guid approvalRequestId) =>
        ApprovalRequests.TryGetValue(approvalRequestId, out var request) ? request : null;

    /// <summary>
    ///     Get approval requests by reservation ID
    /// </summary>
    public IReadOnlyList<ApprovalRequestState> GetApprovalRequestsByReservation(Guid reservationId) =>
        [.. ApprovalRequests.Values.Where(r => GetReservationId(r) == reservationId)];

    private static Guid? GetReservationId(ApprovalRequestState state) => state switch
    {
        ApprovalRequestState.ApprovalRequestPending pending => pending.ReservationId,
        ApprovalRequestState.ApprovalRequestApproved approved => approved.ReservationId,
        ApprovalRequestState.ApprovalRequestRejected rejected => rejected.ReservationId,
        _ => null
    };
}
