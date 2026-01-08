using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Dcb.EventSource.MeetingRoom.Room;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record CommitReservationHold : ICommandWithHandler<CommitReservationHold>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    public bool RequiresApproval { get; init; }

    public Guid? ApprovalRequestId { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        CommitReservationHold command,
        ICommandContext context)
    {
        var tag = new ReservationTag(command.ReservationId);
        var exists = await context.TagExistsAsync(tag);

        if (!exists)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        var reservationStateTyped = await context.GetStateAsync<ReservationState, ReservationProjector>(tag);
        if (reservationStateTyped.Payload is not ReservationState.ReservationDraft draft)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} is not in draft state");
        }

        var roomTag = new RoomTag(command.RoomId);
        var roomStateTyped = await context.GetStateAsync<RoomProjector>(roomTag);
        var roomState = roomStateTyped.Payload as RoomState ?? RoomState.Empty;

        var requiresApproval = roomState.RequiresApproval;
        var approvalRequestId = requiresApproval ? command.ApprovalRequestId : null;

        if (requiresApproval && approvalRequestId == null)
        {
            throw new ApplicationException("Approval request is required for this room");
        }

        var roomReservationsStateTyped = await context.GetStateAsync<RoomReservationsProjector>(roomTag);
        var roomReservationsState = roomReservationsStateTyped.Payload as RoomReservationsState
            ?? RoomReservationsState.Empty;

        if (roomReservationsState.HasConflict(draft.StartTime, draft.EndTime, draft.ReservationId))
        {
            throw new ApplicationException("Reservation time conflicts with another held or confirmed reservation");
        }

        return new ReservationHoldCommitted(
            command.ReservationId,
            command.RoomId,
            draft.OrganizerId,
            draft.OrganizerName,
            draft.StartTime,
            draft.EndTime,
            draft.Purpose,
            requiresApproval,
            approvalRequestId)
            .GetEventWithTags();
    }
}
