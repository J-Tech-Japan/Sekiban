﻿using Microsoft.AspNetCore.Http;
using Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;
namespace Sekiban.EventSourcing.WebHelper.Authorizations
{
    public class Allow<TDefinitionType> : IAuthorizeDefinition where TDefinitionType : IAuthorizationDefinitionType, new()
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
                return AuthorizeResultType.Allowed;
            }
            return AuthorizeResultType.Passed;
        }
    }
}