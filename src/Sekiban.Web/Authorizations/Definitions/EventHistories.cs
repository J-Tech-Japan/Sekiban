namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group for event histories
/// </summary>
public class EventHistories : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.EventHistory;
}
