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
    ///     Day index for active reservations.
    ///     Limits conflict checks to the touched day buckets instead of scanning the whole room history.
    /// </summary>
    public Dictionary<DateOnly, Dictionary<Guid, ReservationSlot>> ActiveReservationsByDay { get; init; } = [];

    /// <summary>
    ///     Checks if a time slot conflicts with any existing reservations.
    /// </summary>
    public bool HasConflict(DateTime startTime, DateTime endTime, Guid? excludeReservationId = null)
    {
        foreach (var bucket in EnumerateDayBuckets(startTime, endTime))
        {
            foreach (var (reservationId, slot) in bucket)
            {
                if (excludeReservationId.HasValue && reservationId == excludeReservationId.Value)
                    continue;

                if (startTime < slot.EndTime && slot.StartTime < endTime)
                {
                    return true;
                }
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

        foreach (var bucket in EnumerateDayBuckets(startTime, endTime))
        {
            foreach (var (reservationId, slot) in bucket)
            {
                if (excludeReservationId.HasValue && reservationId == excludeReservationId.Value)
                    continue;

                if (startTime < slot.EndTime && slot.StartTime < endTime)
                {
                    conflicts.Add((reservationId, slot));
                }
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
        if (ActiveReservations.TryGetValue(reservationId, out var existing))
        {
            RemoveFromDayBuckets(reservationId, existing.StartTime, existing.EndTime);
        }

        var slot = new ReservationSlot(startTime, endTime, purpose, organizerId, status);
        ActiveReservations[reservationId] = slot;
        AddToDayBuckets(reservationId, slot);
        return this;
    }

    /// <summary>
    ///     Removes an active reservation.
    /// </summary>
    public RoomReservationsState RemoveReservation(Guid reservationId)
    {
        if (!ActiveReservations.Remove(reservationId, out var existing))
        {
            return this;
        }

        RemoveFromDayBuckets(reservationId, existing.StartTime, existing.EndTime);
        return this;
    }

    private IEnumerable<Dictionary<Guid, ReservationSlot>> EnumerateDayBuckets(DateTime startTime, DateTime endTime)
    {
        foreach (var day in EnumerateDays(startTime, endTime))
        {
            if (ActiveReservationsByDay.TryGetValue(day, out var bucket))
            {
                yield return bucket;
            }
        }
    }

    private void AddToDayBuckets(Guid reservationId, ReservationSlot slot)
    {
        foreach (var day in EnumerateDays(slot.StartTime, slot.EndTime))
        {
            if (!ActiveReservationsByDay.TryGetValue(day, out var bucket))
            {
                bucket = [];
                ActiveReservationsByDay[day] = bucket;
            }

            bucket[reservationId] = slot;
        }
    }

    private void RemoveFromDayBuckets(Guid reservationId, DateTime startTime, DateTime endTime)
    {
        foreach (var day in EnumerateDays(startTime, endTime))
        {
            if (!ActiveReservationsByDay.TryGetValue(day, out var bucket))
            {
                continue;
            }

            bucket.Remove(reservationId);
            if (bucket.Count == 0)
            {
                ActiveReservationsByDay.Remove(day);
            }
        }
    }

    private static IEnumerable<DateOnly> EnumerateDays(DateTime startTime, DateTime endTime)
    {
        var startDay = DateOnly.FromDateTime(startTime);
        var lastMoment = endTime <= startTime ? startTime : endTime.AddTicks(-1);
        var endDay = DateOnly.FromDateTime(lastMoment);

        for (var day = startDay; day <= endDay; day = day.AddDays(1))
        {
            yield return day;
        }
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
    ReservationSlotStatus Status)
{
    // Parameterless constructor for JSON deserialization
    public ReservationSlot() : this(DateTime.MinValue, DateTime.MinValue, string.Empty, Guid.Empty, ReservationSlotStatus.Held) { }
}

/// <summary>
///     Status of a reservation slot for conflict detection purposes.
/// </summary>
public enum ReservationSlotStatus
{
    Held,
    Confirmed
}
