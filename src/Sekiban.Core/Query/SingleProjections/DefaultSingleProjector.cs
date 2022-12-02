using Sekiban.Core.Aggregate;

namespace Sekiban.Core.Query.SingleProjections;

public class DefaultSingleProjector<TAggregatePayload> : ISingleProjector<Aggregate<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayload, new()
{
    public Aggregate<TAggregatePayload> CreateInitialAggregate(Guid aggregateId)
    {
        return AggregateCommon.Create<Aggregate<TAggregatePayload>>(aggregateId);
    }

    public Type OriginalAggregateType()
    {
        return typeof(TAggregatePayload);
    }
}
