namespace Sekiban.Addon.Web.Authorizations.Definitions;

public class AllGetList : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType)
    {
        return authorizeMethodType == AuthorizeMethodType.Get || authorizeMethodType == AuthorizeMethodType.List;
    }
}
