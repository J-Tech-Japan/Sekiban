namespace Dcb.EventSource.MeetingRoom.Reservation;

/// <summary>
///     Who may act on a reservation. Whether the acting user is an administrator comes from the
///     authenticated user's role (set by the API on the command, never from the request body);
///     the domain does not keep its own copy of the administrator role for authorization.
/// </summary>
internal static class ReservationAccessRules
{
    internal static bool IsOrganizerOrAdmin(Guid actingUserId, bool actingUserIsAdmin, Guid organizerId) =>
        actingUserIsAdmin || (actingUserId != Guid.Empty && actingUserId == organizerId);
}
