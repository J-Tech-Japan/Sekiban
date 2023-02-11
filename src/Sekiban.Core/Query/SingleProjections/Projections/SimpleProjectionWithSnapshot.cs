using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public class SimpleProjectionWithSnapshot : ISingleProjection
{
    private readonly IDocumentRepository _documentRepository;
    private readonly SekibanAggregateTypes _sekibanAggregateTypes;
    private readonly ISingleProjectionFromInitial singleProjectionFromInitial;
    public SimpleProjectionWithSnapshot(
        IDocumentRepository documentRepository,
        ISingleProjectionFromInitial singleProjectionFromInitial,
        SekibanAggregateTypes sekibanAggregateTypes)
    {
        _documentRepository = documentRepository;
        this.singleProjectionFromInitial = singleProjectionFromInitial;
        _sekibanAggregateTypes = sekibanAggregateTypes;
    }

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TState"></typeparam>
    /// <typeparam name="TProjector"></typeparam>
    /// <returns></returns>
    public async Task<TProjection?> GetAggregateAsync<TProjection, TState, TProjector>(
        Guid aggregateId,
        int? toVersion = null,
        SortableUniqueIdValue? includesSortableUniqueId = null)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection,
        ISingleProjectionStateConvertible<TState>
        where TState : IAggregateCommon
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var projector = new TProjector();
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var payloadVersion = projector.GetPayloadVersionIdentifier();
        var snapshotDocument =
            await _documentRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                projector.GetOriginalAggregatePayloadType(),
                projector.GetPayloadType(),
                payloadVersion);
        var state = snapshotDocument is null ? default : snapshotDocument.ToState<TState>(_sekibanAggregateTypes);
        if (state is not null)
        {
            aggregate.ApplySnapshot(state);
        }
        if (toVersion.HasValue && aggregate.Version >= toVersion.Value)
        {
            return await singleProjectionFromInitial.GetAggregateFromInitialAsync<TProjection, TProjector>(
                aggregateId,
                toVersion.Value);
        }
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            projector.GetOriginalAggregatePayloadType(),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.GetOriginalAggregatePayloadType()),
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
        if (aggregate.Version == 0)
        {
            return default;
        }
        if (toVersion.HasValue && aggregate.Version < toVersion.Value)
        {
            throw new SekibanVersionNotReachToSpecificVersion();
        }
        return aggregate;
    }
}
