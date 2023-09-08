namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group interface
/// </summary>
public interface IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType);
}
