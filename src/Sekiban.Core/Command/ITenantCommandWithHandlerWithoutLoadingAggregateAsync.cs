using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, TCommand>,
    ITenantCommandNextCommon<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ITenantCommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, TCommand>
{
    static string ICommandWithHandlerCommon<TAggregatePayload, TCommand>.GetRootPartitionKey(TCommand command) =>
        TCommand.GetTenantId(command);
}
