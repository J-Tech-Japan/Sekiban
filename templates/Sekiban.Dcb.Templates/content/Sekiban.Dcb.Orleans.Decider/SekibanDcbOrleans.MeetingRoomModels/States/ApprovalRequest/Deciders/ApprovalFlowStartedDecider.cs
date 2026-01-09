using Dcb.MeetingRoomModels.Events.ApprovalRequest;
namespace Dcb.MeetingRoomModels.States.ApprovalRequest.Deciders;

/// <summary>
///     Decider for ApprovalFlowStarted event
/// </summary>
public static class ApprovalFlowStartedDecider
{
    /// <summary>
    ///     Create a new ApprovalRequestPending from ApprovalFlowStarted event
    /// </summary>
    public static ApprovalRequestState.ApprovalRequestPending Create(ApprovalFlowStarted started) =>
        new(
            started.ApprovalRequestId,
            started.ReservationId,
            started.RoomId,
            started.RequesterId,
            started.ApproverIds,
            started.RequestedAt,
            started.RequestComment);

    /// <summary>
    ///     Apply ApprovalFlowStarted event to ApprovalRequestState
    /// </summary>
    public static ApprovalRequestState Evolve(this ApprovalRequestState state, ApprovalFlowStarted started) =>
        state switch
        {
            ApprovalRequestState.ApprovalRequestEmpty => Create(started),
            _ => state // Idempotency: ignore if already created
        };
}
