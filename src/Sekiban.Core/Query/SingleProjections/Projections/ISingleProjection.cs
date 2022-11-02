namespace Sekiban.Core.Query.SingleProjections.Projections;

public interface ISingleProjection
{
    Task<T?> GetAggregateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : ISingleAggregate, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleProjector<T>, new();
}
