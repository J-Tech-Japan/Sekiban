using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Dcb.EventSource.MeetingRoom.Room;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;

namespace Dcb.EventSource.MeetingRoom.Reservation;

public record CreateQuickReservation : ICommandWithHandler<CreateQuickReservation>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [Required]
    public Guid OrganizerId { get; init; }

    public string OrganizerName { get; init; } = string.Empty;

    [Required]
    public DateTime StartTime { get; init; }

    [Required]
    public DateTime EndTime { get; init; }

    [Required]
    [StringLength(500)]
    public string Purpose { get; init; } = string.Empty;

    public Guid? ApprovalRequestId { get; init; }

    public string? ApprovalRequestComment { get; init; }

    public List<string> SelectedEquipment { get; init; } = [];

    public static async Task<EventOrNone> HandleAsync(
        CreateQuickReservation command,
        ICommandContext context)
    {
        if (command.EndTime <= command.StartTime)
        {
            throw new ApplicationException("End time must be after start time");
        }

        if (command.StartTime < DateTime.UtcNow)
        {
            throw new ApplicationException("Cannot create reservation in the past");
        }

        ValidateReservationMonth(command.StartTime, DateTime.UtcNow);

        var roomTag = new RoomTag(command.RoomId);
        var roomStateTyped = await context.GetStateAsync<RoomState, RoomProjector>(roomTag);
        if (roomStateTyped.Payload is not RoomState roomState || roomState.RoomId == Guid.Empty)
        {
            throw new ApplicationException($"Room {command.RoomId} not found");
        }

        var reservationTag = new ReservationTag(command.ReservationId);
        if (await context.TagExistsAsync(reservationTag))
        {
            throw new ApplicationException($"Reservation {command.ReservationId} already exists");
        }

        var selectedEquipment = NormalizeSelectedEquipment(command.SelectedEquipment, roomState.Equipment);
        var requiresApproval = roomState.RequiresApproval;

        var roomReservationsStateTyped = await context.GetStateAsync<RoomReservationsProjector>(
            new RoomReservationTag(command.RoomId));
        var roomReservationsState = roomReservationsStateTyped.Payload as RoomReservationsState
            ?? RoomReservationsState.Empty;

        if (roomReservationsState.HasConflict(command.StartTime, command.EndTime, command.ReservationId))
        {
            throw new ApplicationException("Reservation time conflicts with another held or confirmed reservation");
        }

        await context.AppendEvent(new ReservationDraftCreated(
            command.ReservationId,
            command.RoomId,
            command.OrganizerId,
            command.OrganizerName,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            selectedEquipment).GetEventWithTags());

        Guid? approvalRequestId = null;
        if (requiresApproval)
        {
            approvalRequestId = command.ApprovalRequestId;
            if (approvalRequestId is null)
            {
                throw new ApplicationException("Approval request is required for this room");
            }

            await context.AppendEvent(new ApprovalFlowStarted(
                approvalRequestId.Value,
                command.ReservationId,
                command.RoomId,
                command.OrganizerId,
                [],
                DateTime.UtcNow,
                command.ApprovalRequestComment).GetEventWithTags());
        }

        var holdEvent = new ReservationHoldCommitted(
            command.ReservationId,
            command.RoomId,
            command.OrganizerId,
            command.OrganizerName,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            requiresApproval,
            approvalRequestId,
            requiresApproval ? command.ApprovalRequestComment : null,
            selectedEquipment).GetEventWithTags();

        if (requiresApproval)
        {
            return await context.AppendEvent(holdEvent);
        }

        await context.AppendEvent(holdEvent);
        return await context.AppendEvent(new ReservationConfirmed(
            command.ReservationId,
            command.RoomId,
            command.OrganizerId,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            DateTime.UtcNow,
            null).GetEventWithTags());
    }

    private static void ValidateReservationMonth(DateTime startTime, DateTime nowUtc)
    {
        var startMonth = new DateOnly(startTime.Year, startTime.Month, 1);
        var currentMonth = new DateOnly(nowUtc.Year, nowUtc.Month, 1);
        var nextMonth = currentMonth.AddMonths(1);

        if (startMonth != currentMonth && startMonth != nextMonth)
        {
            throw new ApplicationException("Reservations can only be made for this month or next month.");
        }
    }

    private static List<string> NormalizeSelectedEquipment(
        List<string> selectedEquipment,
        IReadOnlyCollection<string> roomEquipment)
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

        var availableEquipment = roomEquipment
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var invalidSelections = trimmedSelections
            .Where(item => !availableEquipment.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invalidSelections.Count > 0)
        {
            throw new ApplicationException(
                $"Selected equipment is not available for this room: {string.Join(", ", invalidSelections)}");
        }

        return trimmedSelections
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
