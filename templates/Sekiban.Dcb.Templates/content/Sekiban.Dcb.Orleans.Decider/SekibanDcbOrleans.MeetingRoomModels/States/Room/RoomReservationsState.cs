using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.Room;

/// <summary>
///     State that tracks all active reservations for a room.
///     Used for conflict detection when creating new reservations.
/// </summary>
public record RoomReservationsState : ITagStatePayload
{
    public static RoomReservationsState Empty => new();

    /// <summary>
    ///     Active reservations (Held or Confirmed) for this room.
    ///     Key is ReservationId, Value is the time slot details.
    /// </summary>
    public Dictionary<Guid, ReservationSlot> ActiveReservations { get; init; } = [];

    /// <summary>
    ///     Checks if a time slot conflicts with any existing reservations.
    /// </summary>
    public bool HasConflict(DateTime startTime, DateTime endTime, Guid? excludeReservationId = null)
    {
        foreach (var (reservationId, slot) in ActiveReservations)
        {
            // Skip the reservation we're updating (if any)
            if (excludeReservationId.HasValue && reservationId == excludeReservationId.Value)
                continue;

            // Check for overlap: two ranges overlap if start1 < end2 AND start2 < end1
            if (startTime < slot.EndTime && slot.StartTime < endTime)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    ///     Gets all conflicting reservations for a given time slot.
    /// </summary>
    public IReadOnlyList<(Guid ReservationId, ReservationSlot Slot)> GetConflicts(
        DateTime startTime, DateTime endTime, Guid? excludeReservationId = null)
    {
        var conflicts = new List<(Guid, ReservationSlot)>();

        foreach (var (reservationId, slot) in ActiveReservations)
        {
            if (excludeReservationId.HasValue && reservationId == excludeReservationId.Value)
                continue;

            if (startTime < slot.EndTime && slot.StartTime < endTime)
            {
                conflicts.Add((reservationId, slot));
            }
        }

        return conflicts;
    }

    /// <summary>
    ///     Adds or updates an active reservation.
    /// </summary>
    public RoomReservationsState AddOrUpdateReservation(
        Guid reservationId,
        DateTime startTime,
        DateTime endTime,
        string purpose,
        Guid organizerId,
        ReservationSlotStatus status)
    {
        var updated = new Dictionary<Guid, ReservationSlot>(ActiveReservations)
        {
            [reservationId] = new ReservationSlot(startTime, endTime, purpose, organizerId, status)
        };
        return this with { ActiveReservations = updated };
    }

    /// <summary>
    ///     Removes an active reservation.
    /// </summary>
    public RoomReservationsState RemoveReservation(Guid reservationId)
    {
        if (!ActiveReservations.ContainsKey(reservationId))
        {
            return this;
        }

        var updated = new Dictionary<Guid, ReservationSlot>(ActiveReservations);
        updated.Remove(reservationId);
        return this with { ActiveReservations = updated };
    }
}

/// <summary>
///     Represents a time slot for a reservation.
/// </summary>
public record ReservationSlot(
    DateTime StartTime,
    DateTime EndTime,
    string Purpose,
    Guid OrganizerId,
    ReservationSlotStatus Status);

/// <summary>
///     Status of a reservation slot for conflict detection purposes.
/// </summary>
public enum ReservationSlotStatus
{
    Held,
    Confirmed
}
