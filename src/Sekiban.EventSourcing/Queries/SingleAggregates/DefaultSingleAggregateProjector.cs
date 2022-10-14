namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public class DefaultSingleAggregateProjector<T> : ISingleAggregateProjector<T> where T : AggregateBase
{
    public T CreateInitialAggregate(Guid aggregateId)
    {
        return AggregateBase.Create<T>(aggregateId);
    }
    public Type OriginalAggregateType()
    {
        return typeof(T);
    }
}
