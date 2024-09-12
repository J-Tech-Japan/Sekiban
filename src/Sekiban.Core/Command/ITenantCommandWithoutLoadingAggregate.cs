using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ITenantCommandWithoutLoadingAggregate<TAggregatePayload> : ICommandCommon<TAggregatePayload>,
    ICommandWithoutLoadingAggregateCommon,
    ITenantAggregatePayloadCommon<TAggregatePayload>,
    ITenantCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon, ITenantAggregatePayloadCommon<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
