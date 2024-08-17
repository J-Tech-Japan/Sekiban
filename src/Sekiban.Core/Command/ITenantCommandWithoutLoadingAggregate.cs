using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithoutLoadingAggregate<TAggregatePayload> : ICommandWithoutLoadingAggregate<TAggregatePayload>,
    ITenantCommandCommon where TAggregatePayload : IAggregatePayloadCommon
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
