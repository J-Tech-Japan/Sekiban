using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

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
