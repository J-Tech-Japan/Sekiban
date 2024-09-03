using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerForExistingAggregate<TAggregatePayload, in TCommand> :
    ICommandWithHandlerForExistingAggregate<TAggregatePayload, TCommand>,
    ITenantCommandCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommandCommon<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
