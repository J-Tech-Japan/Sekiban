namespace Sekiban.Addon.Web.Authorizations.Definitions;

public class AllListMethod : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType)
    {
        return authorizeMethodType == AuthorizeMethodType.List;
    }
}
