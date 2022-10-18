namespace Sekiban.Addon.Web.Authorizations.Definitions;

public interface IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType);
}
