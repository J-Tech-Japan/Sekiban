using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithoutLoadingAggregate<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithoutLoadingAggregateAbove<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>
{
}
public interface
    ICommandWithHandlerWithoutLoadingAggregateAbove<TAggregatePayload, in TCommand> :
    ICommandWithHandlerCommon<TAggregatePayload, TCommand>,
    ICommandWithoutLoadingAggregateCommon where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>
{
    public static abstract ResultBox<UnitValue> HandleCommand(
        TCommand command,
        ICommandContextWithoutGetState<TAggregatePayload> context);
}
