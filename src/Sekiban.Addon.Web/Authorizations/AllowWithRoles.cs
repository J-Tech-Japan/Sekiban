using Microsoft.AspNetCore.Http;
using Sekiban.Addon.Web.Authorizations.Definitions;
namespace Sekiban.Addon.Web.Authorizations;

public class AllowWithRoles<TDefinitionType, TRoleEnum> : IAuthorizeDefinition
    where TDefinitionType : IAuthorizationDefinitionType, new()
    where TRoleEnum : struct, Enum
{
    public AllowWithRoles(IEnumerable<TRoleEnum> roles)
    {
        Roles = roles.Select(s => Enum.GetName(s)!.ToLower());
    }

    public AllowWithRoles(params TRoleEnum[] roles)
    {
        Roles = roles.Select(role => Enum.GetName(role)!.ToLower());
    }

    public IEnumerable<string> Roles { get; }

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
            if (checkRoles(Roles))
            {
                return AuthorizeResultType.Allowed;
            }
        }
        return AuthorizeResultType.Passed;
    }
}
