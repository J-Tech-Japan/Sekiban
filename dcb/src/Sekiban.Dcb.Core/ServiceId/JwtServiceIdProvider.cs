using Microsoft.AspNetCore.Http;

namespace Sekiban.Dcb.ServiceId;

/// <summary>
///     Provider that extracts ServiceId from a JWT claim.
/// </summary>
public sealed class JwtServiceIdProvider : IServiceIdProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _claimType;

    public JwtServiceIdProvider(IHttpContextAccessor httpContextAccessor, string claimType = "service_id")
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        if (string.IsNullOrWhiteSpace(claimType))
        {
            throw new ArgumentException("Claim type must not be null or whitespace.", nameof(claimType));
        }

        _claimType = claimType;
    }

    public string GetCurrentServiceId()
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HTTP context available.");

        var claim = httpContext.User.FindFirst(_claimType)
            ?? throw new InvalidOperationException($"JWT claim '{_claimType}' not found.");

        if (string.IsNullOrWhiteSpace(claim.Value))
        {
            throw new InvalidOperationException($"JWT claim '{_claimType}' is empty.");
        }

        return ServiceIdValidator.NormalizeAndValidate(claim.Value);
    }
}
