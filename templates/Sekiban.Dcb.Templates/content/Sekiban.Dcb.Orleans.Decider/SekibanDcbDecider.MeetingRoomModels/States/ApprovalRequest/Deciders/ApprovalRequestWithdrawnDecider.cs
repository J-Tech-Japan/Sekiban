using Dcb.MeetingRoomModels.Events.ApprovalRequest;
namespace Dcb.MeetingRoomModels.States.ApprovalRequest.Deciders;

/// <summary>
///     Decider for ApprovalRequestWithdrawn event
/// </summary>
public static class ApprovalRequestWithdrawnDecider
{
    /// <summary>
    ///     Apply ApprovalRequestWithdrawn event to ApprovalRequestState
    /// </summary>
    public static ApprovalRequestState Evolve(this ApprovalRequestState state, ApprovalRequestWithdrawn withdrawn) =>
        state switch
        {
            ApprovalRequestState.ApprovalRequestPending pending => new ApprovalRequestState.ApprovalRequestWithdrawn(
                pending.ApprovalRequestId,
                pending.ReservationId,
                pending.RoomId,
                pending.RequesterId,
                new List<Guid>(pending.ApproverIds),
                pending.RequestedAt,
                pending.RequestComment,
                withdrawn.Reason,
                withdrawn.WithdrawnAt),
            _ => state // Idempotency: only a pending request can be withdrawn
        };
}
