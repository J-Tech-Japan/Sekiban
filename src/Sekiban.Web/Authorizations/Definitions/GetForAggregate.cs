using Sekiban.Core.Aggregate;
namespace Sekiban.Web.Authorizations.Definitions;

public class GetForAggregate<TAggregatePayload> : IAuthorizationDefinitionType where TAggregatePayload : IAggregatePayload
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        typeof(TAggregatePayload).FullName == aggregateType.FullName && authorizeMethodType == AuthorizeMethodType.Get;
}
