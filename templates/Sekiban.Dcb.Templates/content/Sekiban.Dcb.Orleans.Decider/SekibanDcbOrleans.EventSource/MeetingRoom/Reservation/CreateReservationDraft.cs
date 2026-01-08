using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Dcb.EventSource.MeetingRoom.Room;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
using System.Linq;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record CreateReservationDraft : ICommandWithHandler<CreateReservationDraft>
{
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [Required]
    public Guid OrganizerId { get; init; }

    public string? OrganizerName { get; init; }

    [Required]
    public DateTime StartTime { get; init; }

    [Required]
    public DateTime EndTime { get; init; }

    [Required]
    [StringLength(500)]
    public string Purpose { get; init; } = string.Empty;

    public List<string> SelectedEquipment { get; init; } = [];

    public static async Task<EventOrNone> HandleAsync(
        CreateReservationDraft command,
        ICommandContext context)
    {
        var reservationId = command.ReservationId != Guid.Empty ? command.ReservationId : Guid.CreateVersion7();

        // Verify the room exists and get its equipment catalog
        var roomTag = new RoomTag(command.RoomId);
        var roomExists = await context.TagExistsAsync(roomTag);

        if (!roomExists)
        {
            throw new ApplicationException($"Room {command.RoomId} not found");
        }

        var roomStateTyped = await context.GetStateAsync<RoomProjector>(roomTag);
        var roomState = roomStateTyped.Payload as RoomState ?? RoomState.Empty;

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

        var selectedEquipment = NormalizeSelectedEquipment(command.SelectedEquipment, roomState.Equipment);

        return new ReservationDraftCreated(
            reservationId,
            command.RoomId,
            command.OrganizerId,
            command.OrganizerName ?? string.Empty,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            selectedEquipment)
            .GetEventWithTags();
    }

    private static List<string> NormalizeSelectedEquipment(List<string> selectedEquipment, List<string> roomEquipment)
    {
        if (selectedEquipment.Count == 0)
        {
            return [];
        }

        var trimmedSelections = selectedEquipment
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();

        if (trimmedSelections.Count == 0)
        {
            return [];
        }

        var roomEquipmentSet = new HashSet<string>(roomEquipment, StringComparer.OrdinalIgnoreCase);
        var invalidSelections = trimmedSelections
            .Where(item => !roomEquipmentSet.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invalidSelections.Count > 0)
        {
            throw new ApplicationException($"Selected equipment is not available in this room: {string.Join(", ", invalidSelections)}");
        }

        var selectionSet = new HashSet<string>(trimmedSelections, StringComparer.OrdinalIgnoreCase);
        return roomEquipment.Where(item => selectionSet.Contains(item)).ToList();
    }
}
