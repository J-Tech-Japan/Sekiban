using System.Security.Claims;

namespace SekibanDcbDecider.ApiService.Endpoints;

internal static class ReservationOrganizerResolver
{
    internal const string AllowDebugUserHeadersConfigKey = "Benchmark:AllowDebugUserHeaders";
    private const string DebugUserIdHeader = "X-Debug-User-Id";
    private const string DebugDisplayNameHeader = "X-Debug-Display-Name";

    internal static OrganizerResolutionResult Resolve(
        HttpContext httpContext,
        Guid? fallbackOrganizerId = null,
        string? fallbackDisplayName = null)
    {
        var allowDebugOverrides = AllowDebugOverrides(httpContext);
        var debugUserId = httpContext.Request.Headers[DebugUserIdHeader].FirstOrDefault();
        if (allowDebugOverrides && Guid.TryParse(debugUserId, out var benchmarkOrganizerId))
        {
            var debugDisplayName = httpContext.Request.Headers[DebugDisplayNameHeader].FirstOrDefault();
            return OrganizerResolutionResult.Success(
                new OrganizerContext(
                    benchmarkOrganizerId,
                    debugDisplayName ?? fallbackDisplayName ?? "Benchmark User"));
        }

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var organizerId))
        {
            if (allowDebugOverrides && fallbackOrganizerId is Guid debugOrganizerId)
            {
                return OrganizerResolutionResult.Success(
                    new OrganizerContext(
                        debugOrganizerId,
                        fallbackDisplayName ?? "Benchmark User"));
            }

            return OrganizerResolutionResult.Invalid("Authenticated user is missing a valid NameIdentifier claim.");
        }

        var displayName = httpContext.User.FindFirstValue("display_name")
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? "Unknown User";

        return OrganizerResolutionResult.Success(new OrganizerContext(organizerId, displayName));
    }

    private static bool AllowDebugOverrides(HttpContext httpContext) =>
        httpContext.User.IsInRole("Admin")
        && httpContext.RequestServices
            .GetRequiredService<IConfiguration>()
            .GetValue<bool>(AllowDebugUserHeadersConfigKey);
}

internal readonly record struct OrganizerResolutionResult(OrganizerContext? Organizer, string? Error)
{
    internal bool IsSuccess => Organizer is not null;

    internal static OrganizerResolutionResult Success(OrganizerContext organizer) => new(organizer, null);

    internal static OrganizerResolutionResult Invalid(string error) => new(null, error);
}

internal readonly record struct OrganizerContext(Guid OrganizerId, string DisplayName);
