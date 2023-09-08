namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group for all list
/// </summary>
public class AllListMethod : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.List;
}
