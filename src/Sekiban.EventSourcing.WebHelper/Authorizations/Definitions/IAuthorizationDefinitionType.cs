namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;

public interface IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType);
}
