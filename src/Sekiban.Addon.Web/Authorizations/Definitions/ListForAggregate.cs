using Sekiban.Core.Aggregate;
namespace Sekiban.Addon.Web.Authorizations.Definitions;

public class ListForAggregate<TAggregate> : IAuthorizationDefinitionType where TAggregate : IAggregate
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType)
    {
        return typeof(TAggregate).FullName == aggregateType.FullName && authorizeMethodType == AuthorizeMethodType.List;
    }
}
