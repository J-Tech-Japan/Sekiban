using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record CancelReservation : ICommandWithHandler<CancelReservation>
{
    [Required]
    public Guid ReservationId { get; init; }

    /// <summary>
    ///     Kept for API compatibility. The room is taken from the reservation itself.
    /// </summary>
    public Guid RoomId { get; init; }

    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    ///     The user asking for the cancellation. Only the organizer or an administrator may cancel.
    ///     The API sets this from the authenticated user; it is not read from the request body.
    /// </summary>
    public Guid RequestedByUserId { get; init; }

    /// <summary>
    ///     Whether the acting user is an administrator. The API always sets this from the authenticated
    ///     user's role and overwrites any value sent by the client.
    /// </summary>
    public bool ActingUserIsAdmin { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        CancelReservation command,
        ICommandContext context)
    {
        var tag = new ReservationTag(command.ReservationId);
        if (!await context.TagExistsAsync(tag))
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        var reservationStateTyped = await context.GetStateAsync<ReservationProjector>(tag);
        var state = reservationStateTyped.Payload as ReservationState ?? ReservationState.Empty;

        var (roomId, organizerId, startTime, endTime) = state switch
        {
            ReservationState.ReservationHeld held => (held.RoomId, held.OrganizerId, held.StartTime, held.EndTime),
            ReservationState.ReservationConfirmed confirmed => (confirmed.RoomId, confirmed.OrganizerId, confirmed.StartTime, confirmed.EndTime),
            ReservationState.ReservationDraft draft => (draft.RoomId, draft.OrganizerId, draft.StartTime, draft.EndTime),
            ReservationState.ReservationEmpty => throw new ApplicationException($"Reservation {command.ReservationId} not found"),
            _ => throw new ApplicationException($"Reservation {command.ReservationId} cannot be cancelled from state: {state.GetType().Name}")
        };

        if (!ReservationAccessRules.IsOrganizerOrAdmin(command.RequestedByUserId, command.ActingUserIsAdmin, organizerId))
        {
            throw new UnauthorizedAccessException("Only the organizer or an administrator can cancel this reservation.");
        }

        var cancelled = new ReservationCancelled(
            command.ReservationId,
            roomId,
            startTime,
            endTime,
            command.Reason,
            DateTime.UtcNow).GetEventWithTags();

        // A held reservation that is still waiting for approval also closes its approval request,
        // in the same command, so the request does not stay pending.
        if (state is ReservationState.ReservationHeld { RequiresApproval: true, ApprovalRequestId: { } approvalRequestId })
        {
            var approvalState = await context.GetStateAsync<ApprovalRequestProjector>(new ApprovalRequestTag(approvalRequestId));
            if (approvalState.Payload is ApprovalRequestState.ApprovalRequestPending)
            {
                await context.AppendEvent(new ApprovalRequestWithdrawn(
                    approvalRequestId,
                    command.ReservationId,
                    command.Reason,
                    DateTime.UtcNow).GetEventWithTags());
            }
        }

        return await context.AppendEvent(cancelled);
    }
}
