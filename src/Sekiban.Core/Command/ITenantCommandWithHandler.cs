using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandler<TAggregatePayload, in TCommand> : ICommandWithHandlerAbove<TAggregatePayload, TCommand>,
    ITenantAggregatePayloadCommon<TAggregatePayload>,
    ITenantCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon, ITenantAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
