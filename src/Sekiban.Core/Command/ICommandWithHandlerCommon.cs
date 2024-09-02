using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerCommon<TAggregatePayload, in TCommand> : ICommandCommon<TAggregatePayload>,
    ICommandWithHandlerCommon where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>
{
    public static abstract Guid SpecifyAggregateId(TCommand command);
}
public interface ICommandWithHandlerCommon;
