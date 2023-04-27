using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.SingleProjections.Projections;
namespace Sekiban.Core.Query.SingleProjections;

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

    public async Task<SingleProjectionState<TSingleProjectionPayload>?>
        AsSingleProjectionStateAsync<TSingleProjectionPayload>(Guid aggregateId, int? toVersion = null, string? includesSortableUniqueId = null)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var aggregate = await _singleProjection
            .GetAggregateAsync<SingleProjection<TSingleProjectionPayload>,
                SingleProjectionState<TSingleProjectionPayload>,
                SingleProjection<TSingleProjectionPayload>>(aggregateId, toVersion, SortableUniqueIdValue.NullableValue(includesSortableUniqueId));
        return aggregate?.ToState();
    }
    public async Task<SingleProjectionState<TSingleProjectionPayload>?> AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
        Guid aggregateId,
        int? toVersion = null) where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var projection =
            await AsSingleProjectionStateFromInitialAsync<SingleProjection<TSingleProjectionPayload>, SingleProjection<TSingleProjectionPayload>>(
                aggregateId,
                toVersion);
        return projection?.ToState();
    }

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public async Task<AggregateState<TAggregatePayload>?> AsDefaultStateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = await AsAggregateFromInitialAsync<TAggregatePayload>(aggregateId, toVersion);
        return aggregate?.ToState();
    }

    public async Task<Aggregate<TAggregatePayload>?> AsAggregateAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
        where TAggregatePayload : IAggregatePayloadCommon => await _singleProjection
        .GetAggregateAsync<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>,
            DefaultSingleProjector<TAggregatePayload>>(
            aggregateId,
            toVersion,
            SortableUniqueIdValue.NullableValue(includesSortableUniqueId));

    public async Task<AggregateState<TAggregatePayload>?> AsDefaultStateAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
        where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = await AsAggregateAsync<TAggregatePayload>(aggregateId, toVersion);
        return aggregate?.GetPayloadTypeIs<TAggregatePayload>() == true ? aggregate?.ToState() : null;
    }

    public async Task<IEnumerable<IEvent>?> AllEventsAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
        where TAggregatePayload : IAggregatePayloadCommon
    {
        var toReturn = new List<IEvent>();
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            typeof(TAggregatePayload),
            PartitionKeyGenerator.ForEvent(aggregateId, typeof(TAggregatePayload)),
            null,
            eventObjects => { toReturn.AddRange(eventObjects); });
        return toVersion is null ? toReturn : toReturn.ToList().Take(toVersion.Value);
    }

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjector"></typeparam>
    /// <returns></returns>
    public async Task<TProjection?> AsSingleProjectionStateFromInitialAsync<TProjection, TProjector>(
        Guid aggregateId,
        int? toVersion)
        where TProjection : IAggregateCommon, ISingleProjection
        where TProjector : ISingleProjector<TProjection>, new() =>
        await singleProjectionFromInitial.GetAggregateFromInitialAsync<TProjection, TProjector>(
            aggregateId,
            toVersion);

    public Task<Aggregate<TAggregatePayload>?> AsAggregateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null)
        where TAggregatePayload : IAggregatePayloadCommon => AsSingleProjectionStateFromInitialAsync<Aggregate<TAggregatePayload>,
        DefaultSingleProjector<TAggregatePayload>>(
        aggregateId,
        toVersion);

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TAggregate"></typeparam>
    /// <typeparam name="TAggregateState"></typeparam>
    /// <typeparam name="TSingleProjector"></typeparam>
    /// <returns></returns>
    private async Task<TAggregateState?> GetAggregateStateAsync<TAggregate, TAggregateState, TSingleProjector>(
        Guid aggregateId,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
        where TAggregate : IAggregateCommon, ISingleProjection, ISingleProjectionStateConvertible<TAggregateState>
        where TAggregateState : IAggregateStateCommon
        where TSingleProjector : ISingleProjector<TAggregate>, new()
    {
        var aggregate =
            await _singleProjection.GetAggregateAsync<TAggregate, TAggregateState, TSingleProjector>(
                aggregateId,
                toVersion,
                SortableUniqueIdValue.NullableValue(includesSortableUniqueId));
        return aggregate is null ? default : aggregate.ToState();
    }
}
