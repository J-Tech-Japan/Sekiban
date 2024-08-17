using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandler<TAggregatePayload, in TCommand> : ICommandWithHandler<TAggregatePayload, TCommand>,
    ITenantCommandCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
