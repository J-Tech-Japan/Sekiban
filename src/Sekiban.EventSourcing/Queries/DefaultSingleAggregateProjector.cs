namespace Sekiban.EventSourcing.Queries;

public class DefaultSingleAggregateProjector<T> : ISingleAggregateProjector<T> where T : AggregateBase
{
    public T CreateInitialAggregate(Guid aggregateId) =>
        AggregateBase.Create<T>(aggregateId);
    public Type OriginalAggregateType() =>
        typeof(T);
}
