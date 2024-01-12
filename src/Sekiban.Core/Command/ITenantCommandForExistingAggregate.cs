using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ITenantCommandForExistingAggregate<TAggregatePayload> : ICommand<TAggregatePayload>, ITenantCommandCommon,
    IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon;