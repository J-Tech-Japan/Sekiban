using Dcb.EventSource.MeetingRoom.Room;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
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
        var (startTime, endTime, purpose, organizerId) = reservationStateTyped.Payload switch
        {
            ReservationState.ReservationHeld held => (held.StartTime, held.EndTime, held.Purpose, held.OrganizerId),
            ReservationState.ReservationDraft draft => (draft.StartTime, draft.EndTime, draft.Purpose, draft.OrganizerId),
            _ => throw new ApplicationException($"Reservation {command.ReservationId} is in invalid state for confirmation: {reservationStateTyped.Payload.GetType().Name}")
        };

        // 3. Check for conflicts on each day the reservation spans
        var dailyTags = RoomDailyActivityTag.CreateTagsForTimeRange(command.RoomId, startTime, endTime).ToList();

        foreach (var dailyTag in dailyTags)
        {
            var dailyStateTyped = await context.GetStateAsync<RoomDailyActivityState, RoomDailyActivityProjector>(dailyTag);
            var dailyState = dailyStateTyped.Payload;

            if (dailyState.HasConflict(startTime, endTime, command.ReservationId))
            {
                var conflicts = dailyState.GetConflicts(startTime, endTime, command.ReservationId);
                var conflictInfo = string.Join(", ", conflicts.Select(c =>
                    $"{c.Slot.StartTime:HH:mm}-{c.Slot.EndTime:HH:mm} ({c.Slot.Purpose})"));
                throw new ApplicationException(
                    $"Time slot conflict detected for room {command.RoomId} on {dailyTag.Date}: {conflictInfo}");
            }
        }

        // 4. Create confirmed event with all details for projectors
        return new ReservationConfirmed(
            command.ReservationId,
            command.RoomId,
            organizerId,
            startTime,
            endTime,
            purpose,
            DateTime.UtcNow).GetEventWithTags();
    }
}
