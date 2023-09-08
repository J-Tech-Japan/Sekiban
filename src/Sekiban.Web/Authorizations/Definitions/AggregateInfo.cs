namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group for aggregate info
/// </summary>
public class AggregateInfo : IAuthorizationDefinitionType
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        authorizeMethodType == AuthorizeMethodType.AggregateInfo;
}
