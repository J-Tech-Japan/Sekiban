using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerAsyncAbove<TAggregatePayload, in TCommand> : ICommandWithHandlerCommon<TAggregatePayload,
    TCommand> where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommandCommon<TAggregatePayload>
{

    public static abstract Task<ResultBox<EventOrNone<TAggregatePayload>>> HandleCommandAsync(
        TCommand command,
        ICommandContext<TAggregatePayload> context);
}
