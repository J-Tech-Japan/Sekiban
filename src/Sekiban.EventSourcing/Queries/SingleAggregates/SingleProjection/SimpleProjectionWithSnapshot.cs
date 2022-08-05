namespace Sekiban.EventSourcing.Queries.SingleAggregates.SingleProjection;

public class SimpleProjectionWithSnapshot : ISingleProjection
{
    private readonly IDocumentRepository _documentRepository;
    private readonly ISingleAggregateFromInitial _singleAggregateFromInitial;

    public SimpleProjectionWithSnapshot(IDocumentRepository documentRepository, ISingleAggregateFromInitial singleAggregateFromInitial)
    {
        _documentRepository = documentRepository;
        _singleAggregateFromInitial = singleAggregateFromInitial;
    }
    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    public async Task<T?> GetAggregateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        var projector = new P();
        var aggregate = projector.CreateInitialAggregate(aggregateId);

        var snapshotDocument = await _documentRepository.GetLatestSnapshotForAggregateAsync(aggregateId, typeof(T));
        var dto = snapshotDocument is null ? default : snapshotDocument.ToDto<Q>();
        if (dto is not null)
        {
            aggregate.ApplySnapshot(dto);
        }
        if (toVersion.HasValue && aggregate.Version >= toVersion.Value)
        {
            return await _singleAggregateFromInitial.GetAggregateFromInitialAsync<T, P>(aggregateId, toVersion.Value);
        }
        await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
            aggregateId,
            projector.OriginalAggregateType(),
            PartitionKeyGenerator.ForAggregateEvent(aggregateId, projector.OriginalAggregateType()),
            dto?.LastSortableUniqueId,
            events =>
            {
                foreach (var e in events)
                {
                    if (!string.IsNullOrWhiteSpace(dto?.LastSortableUniqueId) &&
                        string.CompareOrdinal(dto?.LastSortableUniqueId, e.SortableUniqueId) > 0)
                    {
                        throw new SekibanAggregateEventDuplicateException();
                    }
                    aggregate.ApplyEvent(e);
                    if (toVersion.HasValue && aggregate.Version == toVersion.Value)
                    {
                        break;
                    }
                }
            });
        if (aggregate.Version == 0) { return default; }
        if (toVersion.HasValue && aggregate.Version < toVersion.Value)
        {
            throw new SekibanVersionNotReachToSpecificVersion();
        }
        return aggregate;
    }
}
