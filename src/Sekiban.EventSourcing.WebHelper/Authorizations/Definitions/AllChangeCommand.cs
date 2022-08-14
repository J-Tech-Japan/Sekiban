namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;

public class AllChangeCommand : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.ChangeCommand;
}
