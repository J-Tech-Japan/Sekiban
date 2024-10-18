using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerAsync<TAggregatePayload, in TCommand> : ICommandWithHandlerAsyncAbove<TAggregatePayload,
    TCommand> where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>
{
}
