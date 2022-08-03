namespace Sekiban.EventSourcing.Queries.SingleAggregates.SingleProjection;

public class MemoryCacheSingleProjection : ISingleProjection
{
    private readonly IDocumentRepository _documentRepository;
    public MemoryCacheSingleProjection(IDocumentRepository documentRepository) =>
        _documentRepository = documentRepository;
    public async Task<T?> GetAggregateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new() =>
        throw new NotImplementedException();
}
