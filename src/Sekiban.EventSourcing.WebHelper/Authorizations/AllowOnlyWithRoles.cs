using Microsoft.AspNetCore.Http;
using Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;
namespace Sekiban.EventSourcing.WebHelper.Authorizations;

public class AllowOnlyWithRoles<TDefinitionType, TRoleEnum> : IAuthorizeDefinition where TDefinitionType : IAuthorizationDefinitionType, new()
    where TRoleEnum : struct, Enum
{
    public IEnumerable<string> Roles { get; }
    public AllowOnlyWithRoles(IEnumerable<TRoleEnum> roles)
    {
        Roles = roles.Select(s => Enum.GetName(s)!.ToLower());
    }
    public AllowOnlyWithRoles(TRoleEnum role) =>
        Roles = new List<string> { Enum.GetName(role)!.ToLower() };
    public AllowOnlyWithRoles(TRoleEnum role1, TRoleEnum role2) =>
        Roles = new List<string> { Enum.GetName(role1)!.ToLower(), Enum.GetName(role2)!.ToLower() };
    public AllowOnlyWithRoles(TRoleEnum role1, TRoleEnum role2, TRoleEnum role3) =>
        Roles = new List<string> { Enum.GetName(role1)!.ToLower(), Enum.GetName(role2)!.ToLower(), Enum.GetName(role3)!.ToLower() };

    public AuthorizeResultType Check(
        AuthorizeMethodType authorizeMethodType,
        Type aggregateType,
        Type? commandType,
        Func<IEnumerable<string>, bool> checkRoles,
        HttpContext httpContext)
    {
        if (!new TDefinitionType().IsMatches(authorizeMethodType, aggregateType, commandType))
        {
            return AuthorizeResultType.Passed;
        }
        return checkRoles(Roles) ? AuthorizeResultType.Allowed : AuthorizeResultType.Denied;
    }
}
