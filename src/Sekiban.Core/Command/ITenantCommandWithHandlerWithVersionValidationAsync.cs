using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithVersionValidationAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithVersionValidationAsync<TAggregatePayload, TCommand>,
    ITenantCommandNextCommon<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ITenantCommandWithHandlerWithVersionValidationAsync<TAggregatePayload, TCommand>
{
    static string ICommandWithHandlerCommon<TAggregatePayload, TCommand>.GetRootPartitionKey(TCommand command) =>
        TCommand.GetTenantId(command);
}
