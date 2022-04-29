namespace Sekiban.EventSourcing.Queries;

public interface ISingleAggregateProjectionQueryStore
{
    void SaveProjection(ISingleAggregate aggregate, string typeName);

    TAggregate? FindAggregate<TAggregate>(Guid aggregateId, string typeName)
        where TAggregate : ISingleAggregate;
    public void SaveLatestAggregateList<T>(
        SingleAggregateList<T> singleAggregateList)
        where T : ISingleAggregate;
    public SingleAggregateList<T>? FindAggregateList<T>()
        where T : ISingleAggregate;
}
