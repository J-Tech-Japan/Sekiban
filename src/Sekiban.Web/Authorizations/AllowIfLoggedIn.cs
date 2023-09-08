using Microsoft.AspNetCore.Http;
using Sekiban.Web.Authorizations.Definitions;
namespace Sekiban.Web.Authorizations;

/// <summary>
///     Allow specific authorize group when logged in
/// </summary>
/// <typeparam name="TDefinitionType"></typeparam>
public class AllowIfLoggedIn<TDefinitionType> : IAuthorizeDefinition where TDefinitionType : IAuthorizationDefinitionType, new()
{
    public AuthorizeResultType Check(
        AuthorizeMethodType authorizeMethodType,
        Type aggregateType,
        Type? commandType,
        Func<IEnumerable<string>, bool> checkRoles,
        HttpContext httpContext,
        IServiceProvider serviceProvider)
    {
        if (!new TDefinitionType().IsMatches(authorizeMethodType, aggregateType, commandType))
        {
            return AuthorizeResultType.Passed;
        }
        // Check Authentication
        if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
        {
            return httpContext.User.Identity?.IsAuthenticated ?? false ? AuthorizeResultType.Allowed : AuthorizeResultType.Denied;
        }
        return AuthorizeResultType.Passed;
    }
}
