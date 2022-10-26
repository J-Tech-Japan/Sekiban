namespace Sekiban.Core.Query.SingleAggregate.SingleProjection;

public interface ISingleProjection
{
    Task<T?> GetAggregateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionStateConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new();
}
