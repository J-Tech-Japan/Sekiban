using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithoutLoadingAggregateAsyncAbove<TAggregatePayload, in TCommand> :
    ICommandWithHandlerCommon<TAggregatePayload, TCommand>,
    ICommandWithoutLoadingAggregateCommon where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>
{
    public static abstract Task<ResultBox<EventOrNone<TAggregatePayload>>> HandleCommandAsync(
        TCommand command,
        ICommandContextWithoutGetState<TAggregatePayload> context);
}
