using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
namespace Sekiban.Core.Query.SingleProjections.Projections;

/// <summary>
///     Single projection implementation. Simple one without snapshot.
/// </summary>
public class SimpleProjectionWithSnapshot : ISingleProjection
{
    private readonly IDocumentRepository _documentRepository;
    private readonly ISingleProjectionFromInitial singleProjectionFromInitial;

    public SimpleProjectionWithSnapshot(
        IDocumentRepository documentRepository,
        ISingleProjectionFromInitial singleProjectionFromInitial)
    {
        _documentRepository = documentRepository;
        this.singleProjectionFromInitial = singleProjectionFromInitial;
    }

    /// <summary>
    ///     The normal version that uses snapshots and memory cache.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TState"></typeparam>
    /// <typeparam name="TProjector"></typeparam>
    /// <returns></returns>
    public async Task<TProjection?> GetAggregateAsync<TProjection, TState, TProjector>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection,
        ISingleProjectionStateConvertible<TState>
        where TState : IAggregateStateCommon
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var projector = new TProjector();
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var payloadVersion = projector.GetPayloadVersionIdentifier();
        var snapshotDocument = await _documentRepository.GetLatestSnapshotForAggregateAsync(
            aggregateId,
            projector.GetOriginalAggregatePayloadType(),
            projector.GetPayloadType(),
            rootPartitionKey,
            payloadVersion);
        IAggregateStateCommon? state = null;
        if (snapshotDocument is not null && aggregate.CanApplySnapshot(snapshotDocument.Snapshot))
        {
            aggregate.ApplySnapshot(snapshotDocument?.Snapshot);
        }
        if (toVersion.HasValue && aggregate.Version >= toVersion.Value)
        {
            return await singleProjectionFromInitial.GetAggregateFromInitialAsync<TProjection, TProjector>(
                aggregateId,
                rootPartitionKey,
                toVersion.Value);
        }
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            projector.GetOriginalAggregatePayloadType(),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.GetOriginalAggregatePayloadType(), rootPartitionKey),
            state?.LastSortableUniqueId,
            rootPartitionKey,
            events =>
            {
                foreach (var e in events)
                {
                    if (!string.IsNullOrWhiteSpace(state?.LastSortableUniqueId) &&
                        string.CompareOrdinal(state.LastSortableUniqueId, e.SortableUniqueId) > 0)
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

        return (aggregate?.Version, toVersion) switch
        {
            (0, _) => default,
            (int a, int t) when a < t => throw new SekibanVersionNotReachToSpecificVersion(),
            _ => aggregate
        };
    }
}
