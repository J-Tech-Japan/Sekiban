namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public class DefaultSingleAggregateProjector<T> : ISingleAggregateProjector<T> where T : AggregateCommonBase
{
    public T CreateInitialAggregate(Guid aggregateId)
    {
        return AggregateCommonBase.Create<T>(aggregateId);
    }
    public Type OriginalAggregateType()
    {
        return typeof(T);
    }
}
