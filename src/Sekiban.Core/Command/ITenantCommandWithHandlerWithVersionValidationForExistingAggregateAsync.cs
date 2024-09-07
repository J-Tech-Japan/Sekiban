using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithVersionValidationForExistingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithVersionValidationForExistingAggregateAsync<TAggregatePayload, TCommand>,
    ITenantCommandNextCommon<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ITenantCommandWithHandlerWithVersionValidationForExistingAggregateAsync<TAggregatePayload,
        TCommand>
{
    static string ICommandWithHandlerCommon<TAggregatePayload, TCommand>.GetRootPartitionKey(TCommand command) =>
        TCommand.GetTenantId(command);
}
