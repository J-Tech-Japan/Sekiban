namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group for all get
/// </summary>
public class AllGetMethod : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.Get;
}
