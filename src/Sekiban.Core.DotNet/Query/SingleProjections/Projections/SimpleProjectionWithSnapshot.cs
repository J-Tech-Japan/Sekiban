using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Exceptions;
namespace Sekiban.Core.Query.SingleProjections.Projections;

/// <summary>
///     Single projection implementation. Simple one without snapshot.
/// </summary>
public class SimpleProjectionWithSnapshot(
    EventRepository eventRepository,
    IDocumentRepository documentRepository,
    ISingleProjectionFromInitial singleProjectionFromInitial) : ISingleProjection
{

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
        var snapshotDocument = await documentRepository.GetLatestSnapshotForAggregateAsync(
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
        await eventRepository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                rootPartitionKey,
                new AggregateTypeStream(projector.GetOriginalAggregatePayloadType()),
                aggregateId,
                ISortableIdCondition.FromState(state)),
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
