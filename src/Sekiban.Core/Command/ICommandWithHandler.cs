using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandler<TAggregatePayload, in TCommand> : ICommandWithHandlerAbove<TAggregatePayload, TCommand>,
    IAggregatePayloadCommon<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>;
public interface
    ICommandWithHandlerAbove<TAggregatePayload, in TCommand> : ICommandWithHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
    public static abstract ResultBox<UnitValue> HandleCommand(
        TCommand command,
        ICommandContext<TAggregatePayload> context);
}
