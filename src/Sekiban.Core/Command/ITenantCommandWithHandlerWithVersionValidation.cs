using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithVersionValidation<TAggregatePayload, in TCommand> :
    ITenantCommandWithHandler<TAggregatePayload, TCommand>,
    IVersionValidationCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon, ITenantAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
