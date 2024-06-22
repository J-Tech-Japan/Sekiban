using Microsoft.AspNetCore.Http;
using Sekiban.Web.Authorizations.Definitions;
namespace Sekiban.Web.Authorizations;

/// <summary>
///     Deny specific authorize group
/// </summary>
/// <typeparam name="TDefinitionType"></typeparam>
public class Deny<TDefinitionType> : IAuthorizeDefinition
    where TDefinitionType : IAuthorizationDefinitionType, new()
{
    public async Task<AuthorizeResultType> Check(
        AuthorizeMethodType authorizeMethodType,
        Type aggregateType,
        Type? commandType,
        Func<IEnumerable<string>, Task<bool>> checkRoles,
        HttpContext httpContext,
        IServiceProvider serviceProvider)
    {
        await Task.CompletedTask;
        return new TDefinitionType().IsMatches(authorizeMethodType, aggregateType, commandType)
            ? AuthorizeResultType.Denied
            : AuthorizeResultType.Passed;
    }
}
