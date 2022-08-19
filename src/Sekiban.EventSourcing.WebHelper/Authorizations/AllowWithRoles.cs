using Microsoft.AspNetCore.Http;
using Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;
namespace Sekiban.EventSourcing.WebHelper.Authorizations;

public class AllowWithRoles<TDefinitionType, TRoleEnum> : IAuthorizeDefinition where TDefinitionType : IAuthorizationDefinitionType, new()
    where TRoleEnum : struct, Enum
{
    public IEnumerable<string> Roles { get; }
    public AllowWithRoles(IEnumerable<TRoleEnum> roles)
    {
        Roles = roles.Select(s => Enum.GetName(s)!.ToLower());
    }
    public AllowWithRoles(params TRoleEnum[] roles) =>
        Roles = roles.Select(role => Enum.GetName(role)!.ToLower());
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
