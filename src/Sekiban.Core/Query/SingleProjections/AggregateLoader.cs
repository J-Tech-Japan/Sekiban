using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.SingleProjections.Projections;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Aggregate Loader implementation.
///     Aggregate developer uses <see cref="IAggregateLoader" /> to load aggregate.
/// </summary>
public class AggregateLoader : IAggregateLoader
{
    private readonly IDocumentRepository _documentRepository;
    private readonly Projections.ISingleProjection _singleProjection;
    private readonly ISingleProjectionFromInitial singleProjectionFromInitial;

    public AggregateLoader(
        Projections.ISingleProjection singleProjection,
        ISingleProjectionFromInitial singleProjectionFromInitial,
        IDocumentRepository documentRepository)
    {
        _singleProjection = singleProjection;
        this.singleProjectionFromInitial = singleProjectionFromInitial;
        _documentRepository = documentRepository;
    }

    public async Task<SingleProjectionState<TSingleProjectionPayload>?> AsSingleProjectionStateAsync<TSingleProjectionPayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var aggregate = await _singleProjection
            .GetAggregateAsync<SingleProjection<TSingleProjectionPayload>, SingleProjectionState<TSingleProjectionPayload>,
                SingleProjection<TSingleProjectionPayload>>(
                aggregateId,
                rootPartitionKey,
                toVersion,
                SortableUniqueIdValue.NullableValue(includesSortableUniqueId));
        return aggregate?.ToState();
    }
    public async Task<SingleProjectionState<TSingleProjectionPayload>?> AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var projection
            = await AsSingleProjectionStateFromInitialAsync<SingleProjection<TSingleProjectionPayload>, SingleProjection<TSingleProjectionPayload>>(
                aggregateId,
                rootPartitionKey,
                toVersion);
        return projection?.ToState();
    }

    public async Task<AggregateState<TAggregatePayload>?> AsDefaultStateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommonBase
    {
        var aggregate = await AsAggregateFromInitialAsync<TAggregatePayload>(aggregateId, rootPartitionKey, toVersion);
        return aggregate?.ToState();
    }

    public async Task<Aggregate<TAggregatePayload>?> AsAggregateAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null) where TAggregatePayload : IAggregatePayloadCommonBase =>
        await _singleProjection
            .GetAggregateAsync<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>(
                aggregateId,
                rootPartitionKey,
                toVersion,
                SortableUniqueIdValue.NullableValue(includesSortableUniqueId));

    public async Task<AggregateState<TAggregatePayload>?> AsDefaultStateAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null) where TAggregatePayload : IAggregatePayloadCommonBase
    {
        var aggregate = await AsAggregateAsync<TAggregatePayload>(aggregateId, rootPartitionKey, toVersion);
        return aggregate?.GetPayloadTypeIs<TAggregatePayload>() == true ? aggregate.ToState() : null;
    }

    public async Task<IEnumerable<IEvent>?> AllEventsAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null) where TAggregatePayload : IAggregatePayloadCommonBase
    {
        var toReturn = new List<IEvent>();
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            typeof(TAggregatePayload),
            PartitionKeyGenerator.ForEvent(aggregateId, typeof(TAggregatePayload), rootPartitionKey),
            null,
            rootPartitionKey,
            eventObjects => { toReturn.AddRange(eventObjects); });
        return toVersion is null ? toReturn : toReturn.ToList().Take(toVersion.Value);
    }


    public async Task<TProjection?> AsSingleProjectionStateFromInitialAsync<TProjection, TProjector>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TProjection : IAggregateCommon, ISingleProjection where TProjector : ISingleProjector<TProjection>, new() =>
        await singleProjectionFromInitial.GetAggregateFromInitialAsync<TProjection, TProjector>(aggregateId, rootPartitionKey, toVersion);

    public Task<Aggregate<TAggregatePayload>?> AsAggregateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommonBase =>
        AsSingleProjectionStateFromInitialAsync<Aggregate<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>(
            aggregateId,
            rootPartitionKey,
            toVersion);
}
