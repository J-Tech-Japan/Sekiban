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
        var reservationStateTyped = await context.GetStateAsync<ReservationState, ReservationProjector>(reservationTag);

        if (reservationStateTyped.Payload is ReservationState.ReservationEmpty)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        // 2. Extract reservation details
        if (reservationStateTyped.Payload is not ReservationState.ReservationHeld held)
        {
            throw new ApplicationException(
                $"Reservation {command.ReservationId} is in invalid state for confirmation: {reservationStateTyped.Payload.GetType().Name}");
        }

        var startTime = held.StartTime;
        var endTime = held.EndTime;
        var purpose = held.Purpose;
        var organizerId = held.OrganizerId;

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

        // 3. Get room name for user-friendly error messages
        var roomTag = new RoomTag(command.RoomId);
        var roomStateTyped = await context.GetStateAsync<RoomProjector>(roomTag);
        var roomState = roomStateTyped.Payload as RoomState ?? RoomState.Empty;
        var roomName = !string.IsNullOrEmpty(roomState.Name) ? roomState.Name : $"Room {command.RoomId}";

        // 4. Check for conflicts on each day the reservation spans
        var dailyTags = RoomDailyActivityTag.CreateTagsForTimeRange(command.RoomId, startTime, endTime).ToList();

        foreach (var dailyTag in dailyTags)
        {
            // Use non-typed version to handle case where no events exist for this tag yet
            var dailyStateTyped = await context.GetStateAsync<RoomDailyActivityProjector>(dailyTag);

            // If no events exist, payload will be EmptyTagStatePayload - treat as empty state (no conflicts)
            var dailyState = dailyStateTyped.Payload as RoomDailyActivityState
                ?? RoomDailyActivityState.Empty;

            if (dailyState.HasConflict(startTime, endTime, command.ReservationId))
            {
                var conflicts = dailyState.GetConflicts(startTime, endTime, command.ReservationId);
                var conflictInfo = string.Join(", ", conflicts.Select(c =>
                    $"{c.Slot.StartTime:HH:mm}-{c.Slot.EndTime:HH:mm} ({c.Slot.Purpose})"));
                throw new ApplicationException(
                    $"Time slot conflict detected for '{roomName}' on {dailyTag.Date:yyyy-MM-dd}: {conflictInfo}");
            }
        }

        // 5. Create confirmed event with all details for projectors
        return new ReservationConfirmed(
            command.ReservationId,
            command.RoomId,
            organizerId,
            startTime,
            endTime,
            purpose,
            DateTime.UtcNow,
            approvalDecisionComment).GetEventWithTags();
    }
}
