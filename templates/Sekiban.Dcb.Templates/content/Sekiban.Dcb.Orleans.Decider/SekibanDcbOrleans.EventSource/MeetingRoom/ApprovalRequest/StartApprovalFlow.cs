using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.ApprovalRequest;

public record StartApprovalFlow : ICommandWithHandler<StartApprovalFlow>
{
    public Guid ApprovalRequestId { get; init; }

    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [Required]
    public Guid RequesterId { get; init; }

    [Required]
    [MinLength(1)]
    public List<Guid> ApproverIds { get; init; } = [];

    public static async Task<EventOrNone> HandleAsync(
        StartApprovalFlow command,
        ICommandContext context)
    {
        var approvalRequestId = command.ApprovalRequestId != Guid.Empty
            ? command.ApprovalRequestId
            : Guid.CreateVersion7();

        var tag = new ApprovalRequestTag(approvalRequestId);
        var exists = await context.TagExistsAsync(tag);

        if (exists)
        {
            throw new ApplicationException($"ApprovalRequest {approvalRequestId} already exists");
        }

        // Verify reservation exists
        var reservationTag = new ReservationTag(command.ReservationId);
        var reservationExists = await context.TagExistsAsync(reservationTag);
        if (!reservationExists)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        return new ApprovalFlowStarted(
            approvalRequestId,
            command.ReservationId,
            command.RoomId,
            command.RequesterId,
            command.ApproverIds,
            DateTime.UtcNow)
            .GetEventWithTags();
    }
}
