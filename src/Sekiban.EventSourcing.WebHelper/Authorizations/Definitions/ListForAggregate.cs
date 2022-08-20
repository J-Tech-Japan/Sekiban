using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions
{
    public class ListForAggregate<TAggregate> : IAuthorizationDefinitionType where TAggregate : IAggregate
    {
        public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
            typeof(TAggregate).FullName == aggregateType.FullName && authorizeMethodType == AuthorizeMethodType.List;
    }
}
