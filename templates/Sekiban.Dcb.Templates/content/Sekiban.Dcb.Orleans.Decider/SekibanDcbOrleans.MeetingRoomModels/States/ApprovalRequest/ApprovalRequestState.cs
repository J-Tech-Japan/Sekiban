using System.Text.Json.Serialization;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.ApprovalRequest;

/// <summary>
///     ApprovalRequest state using discriminated union pattern.
/// </summary>
[JsonDerivedType(typeof(ApprovalRequestEmpty), nameof(ApprovalRequestEmpty))]
[JsonDerivedType(typeof(ApprovalRequestPending), nameof(ApprovalRequestPending))]
[JsonDerivedType(typeof(ApprovalRequestApproved), nameof(ApprovalRequestApproved))]
[JsonDerivedType(typeof(ApprovalRequestRejected), nameof(ApprovalRequestRejected))]
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
        DateTime RequestedAt,
        string? RequestComment) : ApprovalRequestState
    {
        public ApprovalRequestPending() : this(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, [], DateTime.MinValue, null) { }
    }

    /// <summary>
    ///     Approved state - approval was granted
    /// </summary>
    public record ApprovalRequestApproved(
        Guid ApprovalRequestId,
        Guid ReservationId,
        Guid RoomId,
        Guid RequesterId,
        List<Guid> ApproverIds,
        Guid ApproverId,
        string? Comment,
        DateTime DecidedAt,
        string? RequestComment) : ApprovalRequestState
    {
        public ApprovalRequestApproved() : this(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, [], Guid.Empty, null, DateTime.MinValue, null) { }
    }

    /// <summary>
    ///     Rejected state - approval was denied
    /// </summary>
    public record ApprovalRequestRejected(
        Guid ApprovalRequestId,
        Guid ReservationId,
        Guid RoomId,
        Guid RequesterId,
        List<Guid> ApproverIds,
        Guid ApproverId,
        string? Comment,
        DateTime DecidedAt,
        string? RequestComment) : ApprovalRequestState
    {
        public ApprovalRequestRejected() : this(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, [], Guid.Empty, null, DateTime.MinValue, null) { }
    }
}
