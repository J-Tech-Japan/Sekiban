using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;

public class ChangeCommandsForAggregateMethod<TAggregate> : IAuthorizationDefinitionType where TAggregate : IAggregate
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType)
    {
        return typeof(TAggregate).FullName == aggregateType.FullName && authorizeMethodType == AuthorizeMethodType.ChangeCommand;
    }
}
