using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using SekibanDcbDeciderAws.ApiService.Endpoints;

namespace SekibanDcbOrleans.Unit;

public class ReservationOrganizerResolverTests
{
    [TestCase("User")]
    [TestCase("Admin")]
    public void Resolve_UsesAuthenticatedUserClaims(string role)
    {
        var organizerId = Guid.CreateVersion7();
        var context = CreateHttpContext(
            new Claim(ClaimTypes.NameIdentifier, organizerId.ToString()),
            new Claim("display_name", "Authenticated User"),
            new Claim(ClaimTypes.Role, role));

        var result = ReservationOrganizerResolver.Resolve(context);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Organizer!.Value.OrganizerId, Is.EqualTo(organizerId));
        Assert.That(result.Organizer.Value.DisplayName, Is.EqualTo("Authenticated User"));
    }

    [TestCase(null)]
    [TestCase("invalid")]
    public void Resolve_RejectsMissingOrInvalidUserId(string? userId)
    {
        var context = userId is null ? CreateHttpContext() : CreateHttpContext(new Claim(ClaimTypes.NameIdentifier, userId));

        var result = ReservationOrganizerResolver.Resolve(context);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Is.EqualTo("Authenticated user is missing a valid NameIdentifier claim."));
    }

    [TestCase("Claim Name", "Claim Name")]
    [TestCase(null, "Unknown User")]
    public void Resolve_UsesNameClaimOrDefaultWhenDisplayNameIsMissing(string? name, string expected)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()) };
        if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));

        var result = ReservationOrganizerResolver.Resolve(CreateHttpContext(claims.ToArray()));

        Assert.That(result.Organizer!.Value.DisplayName, Is.EqualTo(expected));
    }

    private static DefaultHttpContext CreateHttpContext(params Claim[] claims) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
    };
}
