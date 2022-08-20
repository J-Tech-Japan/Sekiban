namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions
{
    public class AllGetMethod : IAuthorizationDefinitionType
    {
        public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
            authorizeMethodType == AuthorizeMethodType.Get;
    }
}
