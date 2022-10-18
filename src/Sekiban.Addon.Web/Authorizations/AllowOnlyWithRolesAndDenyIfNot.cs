using Microsoft.AspNetCore.Http;
using Sekiban.Addon.Web.Authorizations.Definitions;
namespace Sekiban.Addon.Web.Authorizations;

public class AllowOnlyWithRolesAndDenyIfNot<TDefinitionType, TRoleEnum> : IAuthorizeDefinition
    where TDefinitionType : IAuthorizationDefinitionType, new() where TRoleEnum : struct, Enum
{
    public IEnumerable<string> Roles { get; }
    public AllowOnlyWithRolesAndDenyIfNot(IEnumerable<TRoleEnum> roles)
    {
        Roles = roles.Select(s => Enum.GetName(s)!.ToLower());
    }
    public AllowOnlyWithRolesAndDenyIfNot(params TRoleEnum[] roles)
    {
        Roles = roles.Select(m => Enum.GetName(m)!.ToLower());
    }

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
        return checkRoles(Roles) ? AuthorizeResultType.Allowed : AuthorizeResultType.Denied;
    }
}
