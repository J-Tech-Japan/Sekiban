using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SekibanDcbDecider.ApiService.Endpoints;

namespace SekibanDcbOrleans.Unit;

public class ReservationOrganizerResolverTests
{
    [Test]
    public void Resolve_UsesAuthenticatedUserWhenDebugHeadersAreNotAllowed()
    {
        var organizerId = Guid.CreateVersion7();
        var httpContext = CreateHttpContext(
            allowDebugHeaders: true,
            roles: ["User"],
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, organizerId.ToString()),
                new Claim("display_name", "Regular User")
            ]);
        httpContext.Request.Headers["X-Debug-User-Id"] = Guid.CreateVersion7().ToString();
        httpContext.Request.Headers["X-Debug-Display-Name"] = "Spoof Attempt";

        var result = ReservationOrganizerResolver.Resolve(
            httpContext,
            Guid.CreateVersion7(),
            "Fallback Name");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Organizer!.Value.OrganizerId, Is.EqualTo(organizerId));
        Assert.That(result.Organizer.Value.DisplayName, Is.EqualTo("Regular User"));
    }

    [Test]
    public void Resolve_UsesDebugHeadersForAdminWhenEnabled()
    {
        var organizerId = Guid.CreateVersion7();
        var httpContext = CreateHttpContext(
            allowDebugHeaders: true,
            roles: ["Admin"],
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
                new Claim("display_name", "Administrator")
            ]);
        httpContext.Request.Headers["X-Debug-User-Id"] = organizerId.ToString();
        httpContext.Request.Headers["X-Debug-Display-Name"] = "Benchmark User";

        var result = ReservationOrganizerResolver.Resolve(httpContext);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Organizer!.Value.OrganizerId, Is.EqualTo(organizerId));
        Assert.That(result.Organizer.Value.DisplayName, Is.EqualTo("Benchmark User"));
    }

    [Test]
    public void Resolve_RejectsFallbackOrganizerIdWithoutDebugAccess()
    {
        var httpContext = CreateHttpContext(
            allowDebugHeaders: false,
            roles: ["User"],
            claims:
            [
                new Claim("display_name", "Regular User")
            ]);

        var result = ReservationOrganizerResolver.Resolve(
            httpContext,
            Guid.CreateVersion7(),
            "Fallback Name");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Is.EqualTo("Authenticated user is missing a valid NameIdentifier claim."));
    }

    [Test]
    public void Resolve_AllowsFallbackOrganizerIdForAdminWhenDebugModeIsEnabled()
    {
        var organizerId = Guid.CreateVersion7();
        var httpContext = CreateHttpContext(
            allowDebugHeaders: true,
            roles: ["Admin"],
            claims:
            [
                new Claim("display_name", "Administrator")
            ]);

        var result = ReservationOrganizerResolver.Resolve(
            httpContext,
            organizerId,
            "Benchmark User");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Organizer!.Value.OrganizerId, Is.EqualTo(organizerId));
        Assert.That(result.Organizer.Value.DisplayName, Is.EqualTo("Benchmark User"));
    }

    private static DefaultHttpContext CreateHttpContext(
        bool allowDebugHeaders,
        string[] roles,
        Claim[] claims)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    ReservationOrganizerResolver.AllowDebugUserHeadersConfigKey,
                    allowDebugHeaders.ToString())
            ])
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .BuildServiceProvider();

        var identity = new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role);
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(identity)
        };
    }
}
