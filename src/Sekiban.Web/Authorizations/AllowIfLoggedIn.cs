﻿using Microsoft.AspNetCore.Http;
using Sekiban.Web.Authorizations.Definitions;
namespace Sekiban.Web.Authorizations;

/// <summary>
///     Allow specific authorize group when logged in
/// </summary>
/// <typeparam name="TDefinitionType"></typeparam>
public class AllowIfLoggedIn<TDefinitionType> : IAuthorizeDefinition
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
        if (!new TDefinitionType().IsMatches(authorizeMethodType, aggregateType, commandType))
        {
            return AuthorizeResultType.Passed;
        }

        return httpContext.User.Identity?.IsAuthenticated switch
        {
            true => AuthorizeResultType.Allowed,
            false => AuthorizeResultType.Denied,
            _ => AuthorizeResultType.Passed
        };
    }
}
