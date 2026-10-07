using System.Security.Claims;

namespace SekibanDcbDeciderAws.ApiService.Endpoints;

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

        return OrganizerResolutionResult.Success(new OrganizerContext(organizerId, displayName));
    }
}

internal readonly record struct OrganizerResolutionResult(OrganizerContext? Organizer, string? Error)
{
    internal bool IsSuccess => Organizer is not null;

    internal static OrganizerResolutionResult Success(OrganizerContext organizer) => new(organizer, null);

    internal static OrganizerResolutionResult Invalid(string error) => new(null, error);
}

internal readonly record struct OrganizerContext(Guid OrganizerId, string DisplayName);
