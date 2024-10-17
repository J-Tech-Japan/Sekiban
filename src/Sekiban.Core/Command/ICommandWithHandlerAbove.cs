using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerAbove<TAggregatePayload, in TCommand> : ICommandWithHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
    public static abstract ResultBox<UnitValue> HandleCommand(
        TCommand command,
        ICommandContext<TAggregatePayload> context);
}
