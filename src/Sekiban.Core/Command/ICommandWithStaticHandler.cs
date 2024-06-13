using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandWithStaticHandler<TAggregatePayload, in TCommand> : ICommandWithStaticHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{

    public static abstract ResultBox<UnitValue> HandleCommand(TCommand command, ICommandContext<TAggregatePayload> context);
}