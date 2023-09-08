namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group for all method
/// </summary>
public class AllMethod : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) => true;
}
