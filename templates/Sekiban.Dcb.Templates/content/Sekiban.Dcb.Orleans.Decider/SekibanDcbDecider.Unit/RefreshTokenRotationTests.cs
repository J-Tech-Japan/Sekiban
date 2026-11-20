using NUnit.Framework;
using SekibanDcbDecider.ApiService.Auth;

namespace SekibanDcbDecider.Unit;

public class RefreshTokenRotationTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task RotatedWithinGrace_ResolvesToValidSuccessor()
    {
        var successor = Token("new");
        var rotated = Rotated("old", "new", Now.AddSeconds(-10));
        var result = await RefreshTokenRotation.FindGraceSuccessorAsync(rotated, Lookup(successor), Now);
        Assert.That(result, Is.SameAs(successor));
    }

    [Test]
    public async Task RotatedTwiceWithinGrace_FollowsTheChain()
    {
        var latest = Token("c");
        var middle = Rotated("b", "c", Now.AddSeconds(-5));
        var rotated = Rotated("a", "b", Now.AddSeconds(-20));
        var result = await RefreshTokenRotation.FindGraceSuccessorAsync(rotated, Lookup(middle, latest), Now);
        Assert.That(result, Is.SameAs(latest));
    }

    [Test]
    public async Task RotatedOutsideGrace_IsRejected()
    {
        var rotated = Rotated("old", "new", Now - RefreshTokenRotation.GracePeriod - TimeSpan.FromSeconds(1));
        var result = await RefreshTokenRotation.FindGraceSuccessorAsync(rotated, Lookup(Token("new")), Now);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RevokedByLogout_IsRejected()
    {
        var loggedOut = Token("old");
        loggedOut.IsRevoked = true;
        loggedOut.RevokedAt = Now.AddSeconds(-1);
        var result = await RefreshTokenRotation.FindGraceSuccessorAsync(loggedOut, Lookup(Token("new")), Now);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SuccessorRevokedByLogout_IsRejected()
    {
        var successor = Token("new");
        successor.IsRevoked = true;
        successor.RevokedAt = Now.AddSeconds(-1);
        var rotated = Rotated("old", "new", Now.AddSeconds(-5));
        var result = await RefreshTokenRotation.FindGraceSuccessorAsync(rotated, Lookup(successor), Now);
        Assert.That(result, Is.Null);
    }

    private static RefreshToken Token(string value) =>
        new() { Token = value, UserId = "user", ExpiresAt = DateTime.UtcNow.AddDays(7) };

    private static RefreshToken Rotated(string value, string replacedBy, DateTime revokedAt)
    {
        var token = Token(value);
        token.IsRevoked = true;
        token.RevokedAt = revokedAt;
        token.ReplacedByToken = replacedBy;
        return token;
    }

    private static Func<string, Task<RefreshToken?>> Lookup(params RefreshToken[] tokens) =>
        value => Task.FromResult(tokens.FirstOrDefault(t => t.Token == value));
}
