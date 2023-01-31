using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public class DefaultSingleProjector<TAggregatePayload> : ISingleProjector<Aggregate<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayload
{
    public Aggregate<TAggregatePayload> CreateInitialAggregate(Guid aggregateId) => AggregateCommon.Create<Aggregate<TAggregatePayload>>(aggregateId);

    public Type GetOriginalAggregatePayloadType() => typeof(TAggregatePayload);
    public Type GetPayloadType() => typeof(TAggregatePayload);
}
