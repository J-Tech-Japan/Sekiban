using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record CreateReservationDraft : ICommandWithHandler<CreateReservationDraft>
{
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [Required]
    public Guid OrganizerId { get; init; }

    [Required]
    public DateTime StartTime { get; init; }

    [Required]
    public DateTime EndTime { get; init; }

    [Required]
    [StringLength(500)]
    public string Purpose { get; init; } = string.Empty;

    public static async Task<EventOrNone> HandleAsync(
        CreateReservationDraft command,
        ICommandContext context)
    {
        var reservationId = command.ReservationId != Guid.Empty ? command.ReservationId : Guid.CreateVersion7();

        // Verify the room exists
        var roomTag = new RoomTag(command.RoomId);
        var roomExists = await context.TagExistsAsync(roomTag);

        if (!roomExists)
        {
            throw new ApplicationException($"Room {command.RoomId} not found");
        }

        // Verify the reservation doesn't already exist
        var reservationTag = new ReservationTag(reservationId);
        var exists = await context.TagExistsAsync(reservationTag);
        if (exists)
        {
            throw new ApplicationException($"Reservation {reservationId} already exists");
        }

        // Validate times
        if (command.EndTime <= command.StartTime)
        {
            throw new ApplicationException("End time must be after start time");
        }

        if (command.StartTime < DateTime.UtcNow)
        {
            throw new ApplicationException("Cannot create reservation in the past");
        }

        return new ReservationDraftCreated(
            reservationId,
            command.RoomId,
            command.OrganizerId,
            command.StartTime,
            command.EndTime,
            command.Purpose)
            .GetEventWithTags();
    }
}
