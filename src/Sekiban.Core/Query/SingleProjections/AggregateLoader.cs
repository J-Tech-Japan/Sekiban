using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections.Projections;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Aggregate Loader implementation.
///     Aggregate developer uses <see cref="IAggregateLoader" /> to load aggregate.
/// </summary>
public class AggregateLoader(
    Projections.ISingleProjection singleProjection,
    ISingleProjectionFromInitial singleProjectionFromInitial,
    EventRepository eventRepository) : IAggregateLoader
{

    public async Task<SingleProjectionState<TSingleProjectionPayload>?>
        AsSingleProjectionStateAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null,
            SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var aggregate = await singleProjection
            .GetAggregateAsync<SingleProjection<TSingleProjectionPayload>,
                SingleProjectionState<TSingleProjectionPayload>, SingleProjection<TSingleProjectionPayload>>(
                aggregateId,
                rootPartitionKey,
                toVersion,
                retrievalOptions);
        return aggregate?.ToState();
    }
    public async Task<SingleProjectionState<TSingleProjectionPayload>?>
        AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var projection
            = await AsSingleProjectionStateFromInitialAsync<SingleProjection<TSingleProjectionPayload>,
                SingleProjection<TSingleProjectionPayload>>(aggregateId, rootPartitionKey, toVersion);
        return projection?.ToState();
    }

    public async Task<AggregateState<TAggregatePayload>?> AsDefaultStateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = await AsAggregateFromInitialAsync<TAggregatePayload>(aggregateId, rootPartitionKey, toVersion);
        return aggregate?.ToState();
    }

    public async Task<Aggregate<TAggregatePayload>?> AsAggregateAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon =>
        await singleProjection
            .GetAggregateAsync<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>,
                DefaultSingleProjector<TAggregatePayload>>(aggregateId, rootPartitionKey, toVersion, retrievalOptions);

    public async Task<AggregateState<TAggregatePayload>?> AsDefaultStateAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = await AsAggregateAsync<TAggregatePayload>(
            aggregateId,
            rootPartitionKey,
            toVersion,
            retrievalOptions);
        return aggregate?.GetPayloadTypeIs<TAggregatePayload>() == true ? aggregate.ToState() : null;
    }

    public async Task<IEnumerable<IEvent>?> AllEventsAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommon
    {
        var toReturn = new List<IEvent>();
        await eventRepository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                rootPartitionKey,
                new AggregateTypeStream(typeof(TAggregatePayload)),
                aggregateId,
                null),
            eventObjects => { toReturn.AddRange(eventObjects); });
        return toVersion is null ? toReturn : toReturn.ToList().Take(toVersion.Value);
    }


    public async Task<TProjection?> AsSingleProjectionStateFromInitialAsync<TProjection, TProjector>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TProjection : IAggregateCommon, ISingleProjection
        where TProjector : ISingleProjector<TProjection>, new() =>
        await singleProjectionFromInitial.GetAggregateFromInitialAsync<TProjection, TProjector>(
            aggregateId,
            rootPartitionKey,
            toVersion);

    public Task<Aggregate<TAggregatePayload>?> AsAggregateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommon =>
        AsSingleProjectionStateFromInitialAsync<Aggregate<TAggregatePayload>,
            DefaultSingleProjector<TAggregatePayload>>(aggregateId, rootPartitionKey, toVersion);
}
