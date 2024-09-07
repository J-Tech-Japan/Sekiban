using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerCommon<TAggregatePayload, in TCommand> : ICommandCommon<TAggregatePayload>,
    ICommandWithHandlerCommon where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>
{
    public virtual static string GetRootPartitionKey(TCommand command) => IDocument.DefaultRootPartitionKey;
    public static abstract Guid SpecifyAggregateId(TCommand command);
}
public interface ICommandWithHandlerCommon;
