using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ITenantCommandWithVersionValidationForExistingAggregate<TAggregatePayload> : ICommandWithVersionValidation<TAggregatePayload>,
    ITenantCommandCommon, IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon;