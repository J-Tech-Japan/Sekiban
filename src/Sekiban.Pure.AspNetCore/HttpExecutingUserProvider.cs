using Microsoft.AspNetCore.Http;
using Sekiban.Pure.Command.Handlers;
namespace Sekiban.Pure.AspNetCore;

public class HttpExecutingUserProvider(IHttpContextAccessor httpContextAccessor) : IExecutingUserProvider
{
    public string GetExecutingUser() => (httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "unknown") +
        "|" +
        (httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "ip unknown");
}