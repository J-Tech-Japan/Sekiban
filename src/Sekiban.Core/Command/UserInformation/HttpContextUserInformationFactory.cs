using Microsoft.AspNetCore.Http;
using System.Security.Claims;
namespace Sekiban.Core.Command.UserInformation;

/// <summary>
///     Getting user information through HttpContext.
///     If you use dotnet authentication, you can get the user information from the HttpContext.
/// </summary>
public class HttpContextUserInformationFactory : IUserInformationFactory
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextUserInformationFactory(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string GetCurrentUserInformation()
    {
        var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        // Get the Identity and return an error if not authenticated.
        var identity = _httpContextAccessor.HttpContext?.User.Identity;
        var userId = identity is null || identity.IsAuthenticated == false
            ? null
            : (identity as ClaimsIdentity)?.Claims.FirstOrDefault(m => m.Properties.FirstOrDefault().Value == "sub")?.Value;
        return $"{userId ?? "Unauthenticated User"} from {ip ?? "ip address not found"}";
    }
}
