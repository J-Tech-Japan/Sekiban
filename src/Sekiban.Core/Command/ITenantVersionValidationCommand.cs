using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ITenantVersionValidationCommand<TAggregatePayload> : IVersionValidationCommand<TAggregatePayload>, ITenantCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon
{
    string ICommandCommon.GetRootPartitionKey() => TenantId;
}
