using Microsoft.AspNetCore.Http;
using Sekiban.Web.Authorizations.Definitions;
namespace Sekiban.Web.Authorizations;

/// <summary>
///     Deny specific authorize group
/// </summary>
/// <typeparam name="TDefinitionType"></typeparam>
public class Deny<TDefinitionType> : IAuthorizeDefinition where TDefinitionType : IAuthorizationDefinitionType, new()
{
    public AuthorizeResultType Check(
        AuthorizeMethodType authorizeMethodType,
        Type aggregateType,
        Type? commandType,
        Func<IEnumerable<string>, bool> checkRoles,
        HttpContext httpContext,
        IServiceProvider serviceProvider)
    {
        if (new TDefinitionType().IsMatches(authorizeMethodType, aggregateType, commandType))
        {
            return AuthorizeResultType.Denied;
        }
        return AuthorizeResultType.Passed;
    }
}
