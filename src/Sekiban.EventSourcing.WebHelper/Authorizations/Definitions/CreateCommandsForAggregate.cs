using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.WebHelper.Authorizations.Definitions
{
    public class CreateCommandsForAggregate<TAggregate> : IAuthorizationDefinitionType where TAggregate : IAggregate
    {
        public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
            typeof(TAggregate).FullName == aggregateType.FullName && authorizeMethodType == AuthorizeMethodType.CreateCommand;
    }
}
