namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;

public class CommandHistories : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.CommandHistory;
}
