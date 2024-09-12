using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for Command for the Tenant
///     Tenant Id will be the root partition key
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public interface ITenantCommand<TAggregatePayload> : ICommandCommon<TAggregatePayload>, ITenantCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon, ITenantAggregatePayloadCommon<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
