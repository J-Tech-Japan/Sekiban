using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, TCommand>,
    ITenantCommandCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => TenantId;
}
