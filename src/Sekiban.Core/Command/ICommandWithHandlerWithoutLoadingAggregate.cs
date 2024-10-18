using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithoutLoadingAggregate<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithoutLoadingAggregateAbove<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>
{
}
