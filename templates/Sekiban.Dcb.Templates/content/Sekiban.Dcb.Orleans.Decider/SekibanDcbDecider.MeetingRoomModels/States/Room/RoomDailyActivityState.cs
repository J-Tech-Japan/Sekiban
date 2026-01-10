using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.Room;

/// <summary>
///     State tracking all confirmed reservations for a room on a specific date.
///     Used as the consistency boundary for conflict detection.
/// </summary>
public record RoomDailyActivityState : ITagStatePayload
{
    public static RoomDailyActivityState Empty => new();

    /// <summary>
    ///     Confirmed reservations for this room on this day.
    ///     Key is ReservationId, Value is the time slot details.
    /// </summary>
    public Dictionary<Guid, ConfirmedTimeSlot> ConfirmedReservations { get; init; } = [];

    /// <summary>
    ///     Checks if a time slot conflicts with any existing confirmed reservations.
    /// </summary>
    public bool HasConflict(DateTime startTime, DateTime endTime, Guid? excludeReservationId = null)
    {
        foreach (var (reservationId, slot) in ConfirmedReservations)
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
    public IReadOnlyList<(Guid ReservationId, ConfirmedTimeSlot Slot)> GetConflicts(
        DateTime startTime, DateTime endTime, Guid? excludeReservationId = null)
    {
        var conflicts = new List<(Guid, ConfirmedTimeSlot)>();

        foreach (var (reservationId, slot) in ConfirmedReservations)
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
    ///     Adds a confirmed reservation to the state.
    /// </summary>
    public RoomDailyActivityState AddReservation(
        Guid reservationId,
        DateTime startTime,
        DateTime endTime,
        string purpose,
        Guid organizerId)
    {
        var newReservations = new Dictionary<Guid, ConfirmedTimeSlot>(ConfirmedReservations)
        {
            [reservationId] = new ConfirmedTimeSlot(startTime, endTime, purpose, organizerId)
        };
        return this with { ConfirmedReservations = newReservations };
    }

    /// <summary>
    ///     Removes a reservation from the state (cancelled or expired).
    /// </summary>
    public RoomDailyActivityState RemoveReservation(Guid reservationId)
    {
        var newReservations = new Dictionary<Guid, ConfirmedTimeSlot>(ConfirmedReservations);
        newReservations.Remove(reservationId);
        return this with { ConfirmedReservations = newReservations };
    }
}

/// <summary>
///     Represents a confirmed time slot for a reservation.
/// </summary>
public record ConfirmedTimeSlot(
    DateTime StartTime,
    DateTime EndTime,
    string Purpose,
    Guid OrganizerId)
{
    // Parameterless constructor for JSON deserialization
    public ConfirmedTimeSlot() : this(DateTime.MinValue, DateTime.MinValue, string.Empty, Guid.Empty) { }
}
