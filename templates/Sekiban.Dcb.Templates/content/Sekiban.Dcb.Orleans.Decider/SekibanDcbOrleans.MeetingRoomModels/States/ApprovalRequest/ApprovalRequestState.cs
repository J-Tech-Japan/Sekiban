using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.ApprovalRequest;

/// <summary>
///     ApprovalRequest state using discriminated union pattern.
/// </summary>
public abstract record ApprovalRequestState : ITagStatePayload
{
    public static ApprovalRequestState Empty => new ApprovalRequestEmpty();

    /// <summary>
    ///     Empty/initial state before approval request is created
    /// </summary>
    public record ApprovalRequestEmpty() : ApprovalRequestState;

    /// <summary>
    ///     Pending state - waiting for approval decision
    /// </summary>
    public record ApprovalRequestPending(
        Guid ApprovalRequestId,
        Guid ReservationId,
        Guid RoomId,
        Guid RequesterId,
        List<Guid> ApproverIds,
        DateTime RequestedAt) : ApprovalRequestState;

    /// <summary>
    ///     Approved state - approval was granted
    /// </summary>
    public record ApprovalRequestApproved(
        Guid ApprovalRequestId,
        Guid ReservationId,
        Guid ApproverId,
        string? Comment,
        DateTime DecidedAt) : ApprovalRequestState;

    /// <summary>
    ///     Rejected state - approval was denied
    /// </summary>
    public record ApprovalRequestRejected(
        Guid ApprovalRequestId,
        Guid ReservationId,
        Guid ApproverId,
        string? Comment,
        DateTime DecidedAt) : ApprovalRequestState;
}
