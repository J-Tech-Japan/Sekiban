using Microsoft.AspNetCore.Http;
namespace Sekiban.Web.Authorizations;

/// <summary>
///     Authorize group definition
/// </summary>
public interface IAuthorizeDefinition
{
    public AuthorizeResultType Check(
        AuthorizeMethodType authorizeMethodType,
        Type aggregateType,
        Type? commandType,
        Func<IEnumerable<string>, bool> checkRoles,
        HttpContext httpContext,
        IServiceProvider serviceProvider);
}
