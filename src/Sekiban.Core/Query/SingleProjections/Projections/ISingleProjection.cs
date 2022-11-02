namespace Sekiban.Core.Query.SingleProjections.Projections;

public interface ISingleProjection
{
    Task<T?> GetAggregateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : IAggregateIdentifier, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<Q>
        where Q : IAggregateIdentifier
        where P : ISingleProjector<T>, new();
}
