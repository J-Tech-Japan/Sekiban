namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;

public class AllCommand : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.CreateCommand || authorizeMethodType == AuthorizeMethodType.ChangeCommand;
}
