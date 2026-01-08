using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.Tags;
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

    public static async Task<EventOrNone> HandleAsync(
        RecordApprovalDecision command,
        ICommandContext context)
    {
        var tag = new ApprovalRequestTag(command.ApprovalRequestId);
        var exists = await context.TagExistsAsync(tag);

        if (!exists)
        {
            throw new ApplicationException($"ApprovalRequest {command.ApprovalRequestId} not found");
        }

        var approvalStateTyped = await context.GetStateAsync<ApprovalRequestState, ApprovalRequestProjector>(tag);
        if (approvalStateTyped.Payload is not ApprovalRequestState.ApprovalRequestPending pending)
        {
            throw new ApplicationException($"ApprovalRequest {command.ApprovalRequestId} is not pending");
        }

        if (pending.ReservationId != command.ReservationId)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} does not match approval request");
        }

        if (pending.ApproverIds.Count > 0 && !pending.ApproverIds.Contains(command.ApproverId))
        {
            throw new ApplicationException($"User {command.ApproverId} is not an authorized approver");
        }

        return new ApprovalDecisionRecorded(
            command.ApprovalRequestId,
            command.ReservationId,
            command.ApproverId,
            command.Decision,
            command.Comment,
            DateTime.UtcNow)
            .GetEventWithTags();
    }
}
