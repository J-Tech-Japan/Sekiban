using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Sekiban.Core.Command.UserInformation;

/// <summary>
///     Get user information from AzureAd
/// </summary>
public class AzureAdUserInformationFactory : IUserInformationFactory
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AzureAdUserInformationFactory(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public string GetCurrentUserInformation()
    {
        var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        // Get the Identity and return an error if not authenticated.
        var identity = _httpContextAccessor.HttpContext?.User.Identity;
        var userId = identity is null || identity.IsAuthenticated == false ? null : (identity as ClaimsIdentity)?.Name;
        return $"{userId ?? "Unauthenticated User"} from {ip ?? "ip address not found"}";
    }
}
