using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandForExistingAggregate<TAggregatePayload> : ICommand<TAggregatePayload>, IAggregateShouldExistCommand
    where TAggregatePayload : IAggregatePayloadCommon;
