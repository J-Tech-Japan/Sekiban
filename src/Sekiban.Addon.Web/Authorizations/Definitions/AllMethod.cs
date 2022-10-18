namespace Sekiban.Addon.Web.Authorizations.Definitions;

public class AllMethod : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType)
    {
        return true;
    }
}
