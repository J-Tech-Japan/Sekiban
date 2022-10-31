using Sekiban.Core.Document;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
namespace Sekiban.Core.Query.SingleProjections.Projections;

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
        where T : ISingleAggregate, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleProjector<T>, new()
    {
        var projector = new P();
        var aggregate = projector.CreateInitialAggregate(aggregateId);

        var snapshotDocument = await _documentRepository.GetLatestSnapshotForAggregateAsync(aggregateId, typeof(T));
        var state = snapshotDocument is null ? default : snapshotDocument.ToState<Q>();
        if (state is not null)
        {
            aggregate.ApplySnapshot(state);
        }
        if (toVersion.HasValue && aggregate.Version >= toVersion.Value)
        {
            return await _singleAggregateFromInitial.GetAggregateFromInitialAsync<T, P>(aggregateId, toVersion.Value);
        }
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            projector.OriginalAggregateType(),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.OriginalAggregateType()),
            state?.LastSortableUniqueId,
            events =>
            {
                foreach (var e in events)
                {
                    if (!string.IsNullOrWhiteSpace(state?.LastSortableUniqueId) &&
                        string.CompareOrdinal(state?.LastSortableUniqueId, e.SortableUniqueId) > 0)
                    {
                        throw new SekibanEventDuplicateException();
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
