using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public class DefaultSingleProjector<TAggregatePayload> : ISingleProjector<AggregateIdentifier<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayload, new()
{
    public AggregateIdentifier<TAggregatePayload> CreateInitialAggregate(Guid aggregateId) =>
        AggregateIdentifierCommonBase.Create<AggregateIdentifier<TAggregatePayload>>(aggregateId);
    public Type OriginalAggregateType() => typeof(TAggregatePayload);
}
