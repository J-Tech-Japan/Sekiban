using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithVersionValidationForExistingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithVersionValidationForExistingAggregateAsync<TAggregatePayload, TCommand>,
    ITenantCommandCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommandCommon<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
