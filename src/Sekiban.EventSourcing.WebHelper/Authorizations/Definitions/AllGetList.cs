namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions
{
    public class AllGetList : IAuthorizationDefinitionType
    {
        public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
            authorizeMethodType == AuthorizeMethodType.Get || authorizeMethodType == AuthorizeMethodType.List;
    }
}
