using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerForExistingAggregate<TAggregatePayload, in TCommand> :
    ITenantCommandWithHandler<TAggregatePayload, TCommand>,
    IAggregateShouldExistCommand
    where TAggregatePayload : IAggregatePayloadCommon, ITenantAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
