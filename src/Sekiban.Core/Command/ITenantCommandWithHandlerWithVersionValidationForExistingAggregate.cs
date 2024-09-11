using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithVersionValidationForExistingAggregate<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithVersionValidationForExistingAggregate<TAggregatePayload, TCommand>,
    ITenantCommandCommon where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
