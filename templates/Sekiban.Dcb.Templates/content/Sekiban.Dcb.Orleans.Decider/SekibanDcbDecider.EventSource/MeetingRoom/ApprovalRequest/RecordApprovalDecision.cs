using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.ApprovalRequest;

public record RecordApprovalDecision : ICommandWithHandler<RecordApprovalDecision>
{
    [Required]
    public Guid ApprovalRequestId { get; init; }

    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid ApproverId { get; init; }

    [Required]
    public ApprovalDecision Decision { get; init; }

    [StringLength(1000)]
    public string? Comment { get; init; }

    /// <summary>
    ///     Records the decision and, in the same command, confirms (Approved) or rejects (Rejected)
    ///     the held reservation. The reservation must be held and waiting for this approval request.
    /// </summary>
    public static Task<EventOrNone> HandleAsync(
        RecordApprovalDecision command,
        ICommandContext context) =>
        ApprovalDecisionFlow.DecideAsync(
            context,
            command.ApprovalRequestId,
            command.ReservationId,
            command.ApproverId,
            command.Decision,
            command.Comment);
}
