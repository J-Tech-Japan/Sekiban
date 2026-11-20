using Dcb.EventSource.MeetingRoom.Room;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Dcb.EventSource.MeetingRoom.Reservation;

/// <summary>
///     Events that move a held reservation to its final state. Shared by the confirm/reject commands and
///     by the approval decision, so a decision and the reservation transition are written by one command.
///     The room always comes from the reservation, never from a request.
/// </summary>
internal static class ReservationTransitions
{
    internal static async Task<EventPayloadWithTags> ConfirmHeldAsync(
        ICommandContext context,
        ReservationState.ReservationHeld held,
        string? approvalDecisionComment)
    {
        var roomId = held.RoomId;

        // Room name for user-friendly error messages
        var roomStateTyped = await context.GetStateAsync<RoomProjector>(new RoomTag(roomId));
        var roomState = roomStateTyped.Payload as RoomState ?? RoomState.Empty;
        var roomName = !string.IsNullOrEmpty(roomState.Name) ? roomState.Name : $"Room {roomId}";

        // Check for conflicts on each day the reservation spans
        foreach (var dailyTag in RoomDailyActivityTag.CreateTagsForTimeRange(roomId, held.StartTime, held.EndTime))
        {
            // Non-typed read: a day without events has an empty payload, which means no conflicts
            var dailyStateTyped = await context.GetStateAsync<RoomDailyActivityProjector>(dailyTag);
            var dailyState = dailyStateTyped.Payload as RoomDailyActivityState ?? RoomDailyActivityState.Empty;

            if (dailyState.HasConflict(held.StartTime, held.EndTime, held.ReservationId))
            {
                var conflicts = dailyState.GetConflicts(held.StartTime, held.EndTime, held.ReservationId);
                var conflictInfo = string.Join(", ", conflicts.Select(c =>
                    $"{c.Slot.StartTime:HH:mm}-{c.Slot.EndTime:HH:mm} ({c.Slot.Purpose})"));
                throw new ApplicationException(
                    $"Time slot conflict detected for '{roomName}' on {dailyTag.Date:yyyy-MM-dd}: {conflictInfo}");
            }
        }

        return new ReservationConfirmed(
            held.ReservationId,
            roomId,
            held.OrganizerId,
            held.StartTime,
            held.EndTime,
            held.Purpose,
            DateTime.UtcNow,
            approvalDecisionComment).GetEventWithTags();
    }

    internal static EventPayloadWithTags RejectHeld(
        ReservationState.ReservationHeld held,
        Guid approvalRequestId,
        string reason)
    {
        var payload = new ReservationRejected(
            held.ReservationId,
            held.RoomId,
            approvalRequestId,
            reason,
            DateTime.UtcNow);

        var tags = new List<ITag>
        {
            new ReservationTag(held.ReservationId),
            new RoomReservationTag(held.RoomId),
            UserMonthlyReservationTag.FromStartTime(held.OrganizerId, held.StartTime)
        };

        return new EventPayloadWithTags(payload, tags);
    }
}
