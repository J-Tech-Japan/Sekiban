using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithVersionValidationForExistingAggregate<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithVersionValidationForExistingAggregate<TAggregatePayload, TCommand>,
    ITenantCommandNextCommon<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ITenantCommandWithHandlerWithVersionValidationForExistingAggregate<TAggregatePayload, TCommand>
{
    static string ICommandWithHandlerCommon<TAggregatePayload, TCommand>.GetRootPartitionKey(TCommand command) =>
        TCommand.GetTenantId(command);
}
