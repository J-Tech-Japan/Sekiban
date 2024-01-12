using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ITenantCommandWithVersionValidation<TAggregatePayload> : ICommandWithVersionValidation<TAggregatePayload>, ITenantCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon
{
    string ICommandCommon.GetRootPartitionKey() => TenantId;
}
