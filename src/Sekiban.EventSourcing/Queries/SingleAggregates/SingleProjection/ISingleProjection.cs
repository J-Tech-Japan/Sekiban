namespace Sekiban.EventSourcing.Queries.SingleAggregates.SingleProjection
{
    public interface ISingleProjection
    {
        Task<T?> GetAggregateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
            where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
            where Q : ISingleAggregate
            where P : ISingleAggregateProjector<T>, new();
    }
}
