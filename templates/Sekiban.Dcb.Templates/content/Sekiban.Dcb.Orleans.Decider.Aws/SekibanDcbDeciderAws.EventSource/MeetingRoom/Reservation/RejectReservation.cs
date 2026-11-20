using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

/// <summary>
///     Rejects a held reservation that is waiting for approval. This is the same as recording a
///     Rejected approval decision: the approval request and the reservation are updated together.
/// </summary>
public record RejectReservation : ICommandWithHandler<RejectReservation>
{
    [Required]
    public Guid ReservationId { get; init; }

    /// <summary>
    ///     Kept for API compatibility. The room is taken from the reservation itself.
    /// </summary>
    public Guid RoomId { get; init; }

    [Required]
    public Guid ApprovalRequestId { get; init; }

    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    ///     The administrator rejecting the reservation (recorded as the approver).
    ///     The API sets this from the authenticated user; it is not read from the request body.
    /// </summary>
    public Guid RejectedByUserId { get; init; }

    public static Task<EventOrNone> HandleAsync(
        RejectReservation command,
        ICommandContext context) =>
        ApprovalDecisionFlow.DecideAsync(
            context,
            command.ApprovalRequestId,
            command.ReservationId,
            command.RejectedByUserId,
            ApprovalDecision.Rejected,
            command.Reason);
}
