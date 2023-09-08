namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group for all get or list
/// </summary>
public class AllGetList : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType is AuthorizeMethodType.Get or AuthorizeMethodType.List;
}
