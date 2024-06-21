using Microsoft.AspNetCore.Http;
namespace Sekiban.Web.Authorizations;

/// <summary>
///     Authorize group definition
/// </summary>
public interface IAuthorizeDefinition
{
    public Task<AuthorizeResultType> Check(
        AuthorizeMethodType authorizeMethodType,
        Type aggregateType,
        Type? commandType,
        Func<IEnumerable<string>, Task<bool>> checkRoles,
        HttpContext httpContext,
        IServiceProvider serviceProvider);
}
