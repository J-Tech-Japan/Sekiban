﻿namespace Sekiban.Addon.Web.Authorizations.Definitions;

public class AllChangeCommand : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType)
    {
        return authorizeMethodType == AuthorizeMethodType.ChangeCommand;
    }
}