using System.Security.Claims;
using Dcb.EventSource.MeetingRoom.Reservation;

namespace SekibanDcbDecider.ApiService.Endpoints;

internal static class ReservationOrganizerResolver
{
    internal static OrganizerResolutionResult Resolve(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var organizerId))
        {
            return OrganizerResolutionResult.Invalid("Authenticated user is missing a valid NameIdentifier claim.");
        }

        var displayName = httpContext.User.FindFirstValue("display_name")
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? "Unknown User";

        // The JWT/cookie Identity role is the only source for "administrator"
        var isAdmin = httpContext.User.IsInRole("Admin");

        return OrganizerResolutionResult.Success(new OrganizerContext(organizerId, displayName, isAdmin));
    }
}

internal static class ActingUserCommands
{
    /// <summary>
    ///     The draft command is bound from the request body, so every acting-user field is overwritten here:
    ///     a client cannot choose the organizer or claim to be an administrator.
    /// </summary>
    internal static CreateReservationDraft ForActingUser(CreateReservationDraft command, OrganizerContext organizer) =>
        command with
        {
            OrganizerId = organizer.OrganizerId,
            OrganizerName = organizer.DisplayName,
            ActingUserIsAdmin = organizer.IsAdmin
        };
}

internal readonly record struct OrganizerResolutionResult(OrganizerContext? Organizer, string? Error)
{
    internal bool IsSuccess => Organizer is not null;

    internal static OrganizerResolutionResult Success(OrganizerContext organizer) => new(organizer, null);

    internal static OrganizerResolutionResult Invalid(string error) => new(null, error);
}

internal readonly record struct OrganizerContext(Guid OrganizerId, string DisplayName, bool IsAdmin);
