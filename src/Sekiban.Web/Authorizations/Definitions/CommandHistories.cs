namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group for command history
/// </summary>
public class CommandHistories : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.CommandHistory;
}
