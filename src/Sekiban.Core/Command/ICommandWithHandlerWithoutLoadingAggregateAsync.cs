using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithoutLoadingAggregateAsyncAbove<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>
{
}
public interface
    ICommandWithHandlerWithoutLoadingAggregateAsyncAbove<TAggregatePayload, in TCommand> :
    ICommandWithHandlerCommon<TAggregatePayload, TCommand>,
    ICommandWithoutLoadingAggregateCommon where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>
{
    public static abstract Task<ResultBox<UnitValue>> HandleCommandAsync(
        TCommand command,
        ICommandContextWithoutGetState<TAggregatePayload> context);
}
