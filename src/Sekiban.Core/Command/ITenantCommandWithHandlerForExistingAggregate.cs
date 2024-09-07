using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerForExistingAggregate<TAggregatePayload, in TCommand> :
    ICommandWithHandlerForExistingAggregate<TAggregatePayload, TCommand>,
    ITenantCommandNextCommon<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ITenantCommandWithHandlerForExistingAggregate<TAggregatePayload, TCommand>
{
    static string ICommandWithHandlerCommon<TAggregatePayload, TCommand>.GetRootPartitionKey(TCommand command) =>
        TCommand.GetTenantId(command);
}
