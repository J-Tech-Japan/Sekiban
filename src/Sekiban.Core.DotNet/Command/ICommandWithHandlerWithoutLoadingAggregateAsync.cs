using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithoutLoadingAggregateAsyncAbove<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>
{
}
