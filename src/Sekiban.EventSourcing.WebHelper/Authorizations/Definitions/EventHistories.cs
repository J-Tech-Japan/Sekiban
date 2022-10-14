namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;

public class EventHistories : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType)
    {
        return authorizeMethodType == AuthorizeMethodType.EventHistory;
    }
}
