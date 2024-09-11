using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerForExistingAggregate<TAggregatePayload, in TCommand> :
    ICommandWithHandler<TAggregatePayload, TCommand>,
    IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
}
