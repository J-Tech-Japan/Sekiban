using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
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

    /// <summary>
    ///     Optional. The room always comes from the draft; a different non-empty value is rejected.
    /// </summary>
    public Guid RoomId { get; init; }

    /// <summary>
    ///     Informational only. Whether approval is needed is decided by the room.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    ///     Optional existing approval request for this reservation (it must be pending and belong to this
    ///     reservation). When the room requires approval and no id is given, the hold starts a new approval flow.
    /// </summary>
    public Guid? ApprovalRequestId { get; init; }

    public string? ApprovalRequestComment { get; init; }

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

        if (command.RoomId != Guid.Empty && command.RoomId != draft.RoomId)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} is not for room {command.RoomId}");
        }

        var roomId = draft.RoomId;
        var roomTag = new RoomTag(roomId);
        var roomStateTyped = await context.GetStateAsync<RoomProjector>(roomTag);
        if (roomStateTyped.Payload is not RoomState roomState || roomState.RoomId == Guid.Empty)
        {
            throw new ApplicationException($"Room {roomId} not found");
        }

        var requiresApproval = roomState.RequiresApproval;
        var approvalRequestComment = requiresApproval ? command.ApprovalRequestComment : null;

        var roomReservationTag = new RoomReservationTag(roomId);
        var roomReservationsStateTyped = await context.GetStateAsync<RoomReservationsProjector>(roomReservationTag);
        var roomReservationsState = roomReservationsStateTyped.Payload as RoomReservationsState
            ?? RoomReservationsState.Empty;

        if (roomReservationsState.HasConflict(draft.StartTime, draft.EndTime, draft.ReservationId))
        {
            throw new ApplicationException("Reservation time conflicts with another held or confirmed reservation");
        }

        Guid? approvalRequestId = null;
        if (requiresApproval)
        {
            if (command.ApprovalRequestId is { } suppliedId)
            {
                await EnsurePendingApprovalForReservationAsync(context, suppliedId, draft.ReservationId);
                approvalRequestId = suppliedId;
            }
            else
            {
                // Derive the approval request on the server so a hold can never point at a request
                // that does not exist (which would leave the reservation stuck in Held).
                approvalRequestId = Guid.CreateVersion7();
                await context.AppendEvent(new ApprovalFlowStarted(
                    approvalRequestId.Value,
                    draft.ReservationId,
                    roomId,
                    draft.OrganizerId,
                    [],
                    DateTime.UtcNow,
                    approvalRequestComment).GetEventWithTags());
            }
        }

        // The approval flow (when started above) and the hold are appended in the same command,
        // so they are written together or not at all.
        return await context.AppendEvent(new ReservationHoldCommitted(
            command.ReservationId,
            roomId,
            draft.OrganizerId,
            draft.OrganizerName,
            draft.StartTime,
            draft.EndTime,
            draft.Purpose,
            requiresApproval,
            approvalRequestId,
            approvalRequestComment,
            draft.SelectedEquipment)
            .GetEventWithTags());
    }

    private static async Task EnsurePendingApprovalForReservationAsync(
        ICommandContext context,
        Guid approvalRequestId,
        Guid reservationId)
    {
        var approvalTag = new ApprovalRequestTag(approvalRequestId);
        if (!await context.TagExistsAsync(approvalTag))
        {
            throw new ApplicationException($"Approval request {approvalRequestId} not found");
        }

        var approvalState = await context.GetStateAsync<ApprovalRequestProjector>(approvalTag);
        if (approvalState.Payload is not ApprovalRequestState.ApprovalRequestPending pending)
        {
            throw new ApplicationException($"Approval request {approvalRequestId} is not pending");
        }

        if (pending.ReservationId != reservationId)
        {
            throw new ApplicationException(
                $"Approval request {approvalRequestId} does not belong to reservation {reservationId}");
        }
    }
}
