using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ITenantOnlyPublishingCommand<TAggregatePayload> : IOnlyPublishingCommand<TAggregatePayload>, ITenantCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon
{
    string ICommandCommon.GetRootPartitionKey() => TenantId;
}
