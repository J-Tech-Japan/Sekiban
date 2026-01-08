using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
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

        return new ReservationHoldCommitted(
            command.ReservationId,
            command.RoomId,
            command.RequiresApproval,
            command.ApprovalRequestId)
            .GetEventWithTags();
    }
}
