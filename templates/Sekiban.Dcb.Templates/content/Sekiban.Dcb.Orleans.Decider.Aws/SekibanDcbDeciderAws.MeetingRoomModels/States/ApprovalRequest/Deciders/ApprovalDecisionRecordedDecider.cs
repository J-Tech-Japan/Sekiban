using Dcb.MeetingRoomModels.Events.ApprovalRequest;
namespace Dcb.MeetingRoomModels.States.ApprovalRequest.Deciders;

/// <summary>
///     Decider for ApprovalDecisionRecorded event
/// </summary>
public static class ApprovalDecisionRecordedDecider
{
    /// <summary>
    ///     Validate preconditions for recording decision
    /// </summary>
    public static void Validate(this ApprovalRequestState.ApprovalRequestPending state, Guid approverId)
    {
        if (state.ApproverIds.Count > 0 && !state.ApproverIds.Contains(approverId))
        {
            throw new InvalidOperationException($"User {approverId} is not an authorized approver");
        }
    }

    /// <summary>
    ///     Apply ApprovalDecisionRecorded event to ApprovalRequestState
    /// </summary>
    public static ApprovalRequestState Evolve(this ApprovalRequestState state, ApprovalDecisionRecorded recorded) =>
        state switch
        {
            ApprovalRequestState.ApprovalRequestPending pending => recorded.Decision switch
            {
                ApprovalDecision.Approved => new ApprovalRequestState.ApprovalRequestApproved(
                    pending.ApprovalRequestId,
                    pending.ReservationId,
                    pending.RoomId,
                    pending.RequesterId,
                    new List<Guid>(pending.ApproverIds),
                    recorded.ApproverId,
                    recorded.Comment,
                    recorded.DecidedAt,
                    pending.RequestComment),
                ApprovalDecision.Rejected => new ApprovalRequestState.ApprovalRequestRejected(
                    pending.ApprovalRequestId,
                    pending.ReservationId,
                    pending.RoomId,
                    pending.RequesterId,
                    new List<Guid>(pending.ApproverIds),
                    recorded.ApproverId,
                    recorded.Comment,
                    recorded.DecidedAt,
                    pending.RequestComment),
                _ => state
            },
            _ => state // Idempotency: ignore if not in pending state
        };
}
