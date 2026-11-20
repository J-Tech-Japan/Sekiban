using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record ConfirmReservation : ICommandWithHandler<ConfirmReservation>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        ConfirmReservation command,
        ICommandContext context)
    {
        // 1. Get the reservation state to get full details
        var reservationTag = new ReservationTag(command.ReservationId);
        if (!await context.TagExistsAsync(reservationTag))
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        var reservationStateTyped = await context.GetStateAsync<ReservationProjector>(reservationTag);
        var reservationState = reservationStateTyped.Payload as ReservationState ?? ReservationState.Empty;

        // 2. Extract reservation details
        if (reservationState is not ReservationState.ReservationHeld held)
        {
            throw new ApplicationException(
                $"Reservation {command.ReservationId} is in invalid state for confirmation: {reservationState.GetType().Name}");
        }

        string? approvalDecisionComment = null;
        if (held.RequiresApproval)
        {
            if (held.ApprovalRequestId == null)
            {
                throw new ApplicationException("Approval request is required before confirmation");
            }

            var approvalTag = new ApprovalRequestTag(held.ApprovalRequestId.Value);
            var approvalStateTyped = await context.GetStateAsync<ApprovalRequestProjector>(approvalTag);
            if (approvalStateTyped.Payload is not ApprovalRequestState.ApprovalRequestApproved approved)
            {
                throw new ApplicationException("Reservation approval has not been granted");
            }

            approvalDecisionComment = approved.Comment;
        }

        // 3. Check the room's daily schedule and build the confirmed event
        return await ReservationTransitions.ConfirmHeldAsync(context, held, approvalDecisionComment);
    }
}
