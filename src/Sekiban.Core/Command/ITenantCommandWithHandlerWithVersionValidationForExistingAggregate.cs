using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithVersionValidationForExistingAggregate<TAggregatePayload, in TCommand> :
    ITenantCommandWithHandler<TAggregatePayload, TCommand>,
    IVersionValidationCommandCommon,
    IAggregateShouldExistCommand
    where TAggregatePayload : IAggregatePayloadCommon, ITenantAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
