namespace Sekiban.Web.Authorizations.Definitions;

public class AggregateInfo : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.AggregateInfo;
}
