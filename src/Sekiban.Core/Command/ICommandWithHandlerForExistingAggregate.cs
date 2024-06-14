using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandWithHandlerForExistingAggregate<TAggregatePayload, in TCommand> : ICommandWithHandlerCommon<TAggregatePayload, TCommand>,
    IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{

    public static abstract ResultBox<UnitValue> HandleCommand(TCommand command, ICommandContext<TAggregatePayload> context);
}
