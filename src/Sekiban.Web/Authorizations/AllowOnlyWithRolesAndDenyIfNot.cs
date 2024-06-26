﻿using Microsoft.AspNetCore.Http;
using Sekiban.Web.Authorizations.Definitions;
namespace Sekiban.Web.Authorizations;

/// <summary>
///     Allow only specific authorize group with specific roles otherwise deny
/// </summary>
/// <typeparam name="TDefinitionType"></typeparam>
/// <typeparam name="TRoleEnum"></typeparam>
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

    public async Task<AuthorizeResultType> Check(
        AuthorizeMethodType authorizeMethodType,
        Type aggregateType,
        Type? commandType,
        Func<IEnumerable<string>, Task<bool>> checkRoles,
        HttpContext httpContext,
        IServiceProvider serviceProvider)
    {
        return (new TDefinitionType().IsMatches(authorizeMethodType, aggregateType, commandType), await checkRoles(Roles)) switch
        {
            (false, _) => AuthorizeResultType.Passed
            ,
            (true, false) => AuthorizeResultType.Denied
            ,
            (true, true) => AuthorizeResultType.Allowed
        };
    }
}
