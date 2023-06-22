using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ITenantCommand<TAggregatePayload> : ICommand<TAggregatePayload>, ITenantCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon
{
    string ICommandCommon.GetRootPartitionKey() => TenantId;
}
