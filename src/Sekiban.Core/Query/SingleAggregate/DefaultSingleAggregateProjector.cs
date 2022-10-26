using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleAggregate;

public class DefaultSingleAggregateProjector<TAggregatePayload> : ISingleAggregateProjector<Aggregate<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayload, new()
{
    public Aggregate<TAggregatePayload> CreateInitialAggregate(Guid aggregateId)
    {
        return AggregateCommonBase.Create<Aggregate<TAggregatePayload>>(aggregateId);
    }
    public Type OriginalAggregateType()
    {
        return typeof(TAggregatePayload);
    }
}
