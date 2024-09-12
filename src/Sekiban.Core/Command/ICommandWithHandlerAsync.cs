using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerAsync<TAggregatePayload, in TCommand> : ICommandWithHandlerAsyncAbove<TAggregatePayload,
    TCommand> where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>
{
}
public interface
    ICommandWithHandlerAsyncAbove<TAggregatePayload, in TCommand> : ICommandWithHandlerCommon<TAggregatePayload,
    TCommand> where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommandCommon<TAggregatePayload>
{

    public static abstract Task<ResultBox<UnitValue>> HandleCommandAsync(
        TCommand command,
        ICommandContext<TAggregatePayload> context);
}
