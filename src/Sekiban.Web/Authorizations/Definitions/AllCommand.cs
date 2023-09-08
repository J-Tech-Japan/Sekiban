namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group for all command
/// </summary>
public class AllCommand : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.Command;
}
