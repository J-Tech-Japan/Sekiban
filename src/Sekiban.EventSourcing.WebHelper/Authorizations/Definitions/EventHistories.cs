namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions
{
    public class EventHistories : IAuthorizationDefinitionType
    {
        public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
            authorizeMethodType == AuthorizeMethodType.EventHistory;
    }
}
