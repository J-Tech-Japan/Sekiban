﻿namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions
{
    public class AllCreateCommand : IAuthorizationDefinitionType
    {
        public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
            authorizeMethodType == AuthorizeMethodType.CreateCommand;
    }
}