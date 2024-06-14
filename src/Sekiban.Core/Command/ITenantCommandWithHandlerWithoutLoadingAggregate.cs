using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithoutLoadingAggregate<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithoutLoadingAggregate<TAggregatePayload, TCommand>,
    ITenantCommandCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => TenantId;
}
