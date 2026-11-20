namespace SekibanDcbDecider.ApiService.Auth;

/// <summary>
///     Refresh tokens are single use: refreshing revokes the presented token and issues a successor.
///     When several frontend instances notice an expired access token at the same time, more than one of
///     them can present the same refresh token. For a short grace period after rotation, the rotated token
///     resolves to its (still valid) successor instead of failing, so the session survives. Outside the
///     grace period, or for a token revoked by logout, the token is rejected as before.
/// </summary>
public static class RefreshTokenRotation
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(60);

    private const int MaxSuccessorHops = 5;

    public static bool IsWithinGrace(RefreshToken token, DateTime nowUtc) =>
        token.IsRevoked
        && token.ReplacedByToken is not null
        && token.RevokedAt is { } revokedAt
        && nowUtc - revokedAt <= GracePeriod
        && nowUtc < token.ExpiresAt;

    /// <summary>
    ///     Returns the valid successor of a token that was rotated within the grace period, following a short
    ///     chain of rotations, or null when the token must be rejected.
    /// </summary>
    public static async Task<RefreshToken?> FindGraceSuccessorAsync(
        RefreshToken rotated,
        Func<string, Task<RefreshToken?>> findByToken,
        DateTime nowUtc)
    {
        var current = rotated;
        for (var hop = 0; hop < MaxSuccessorHops; hop++)
        {
            if (!IsWithinGrace(current, nowUtc))
            {
                return null;
            }

            var successor = await findByToken(current.ReplacedByToken!);
            if (successor is null)
            {
                return null;
            }

            if (successor.IsValid)
            {
                return successor;
            }

            current = successor;
        }

        return null;
    }
}
