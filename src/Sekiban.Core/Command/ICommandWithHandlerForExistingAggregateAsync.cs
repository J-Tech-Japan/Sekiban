using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerForExistingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerAsync<TAggregatePayload, TCommand>,
    IAggregateShouldExistCommand
    where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>
{
}
