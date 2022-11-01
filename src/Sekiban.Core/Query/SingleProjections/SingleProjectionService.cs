using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections.Projections;
namespace Sekiban.Core.Query.SingleProjections;

public class SingleProjectionService : ISingleProjectionService
{
    private readonly ISingleAggregateFromInitial _singleAggregateFromInitial;
    private readonly Projections.ISingleProjection _singleProjection;
    public SingleProjectionService(Projections.ISingleProjection singleProjection, ISingleAggregateFromInitial singleAggregateFromInitial)
    {
        _singleProjection = singleProjection;
        _singleAggregateFromInitial = singleAggregateFromInitial;
    }

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    public async Task<T?> GetAggregateProjectionFromInitialAsync<T, P>(Guid aggregateId, int? toVersion)
        where T : ISingleAggregate, ISingleProjection
        where P : ISingleProjector<T>, new() => await _singleAggregateFromInitial.GetAggregateFromInitialAsync<T, P>(aggregateId, toVersion);
    public Task<Aggregate<TAggregatePayload>?> GetAggregateFromInitialAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new() =>
        GetAggregateProjectionFromInitialAsync<Aggregate<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>(
            aggregateId,
            toVersion);

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public async Task<AggregateState<TAggregatePayload>?> GetAggregateStateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null) where TAggregatePayload : IAggregatePayload, new()
    {
        var aggregate = await GetAggregateFromInitialAsync<TAggregatePayload>(aggregateId, toVersion);
        return aggregate?.ToState();
    }

    public async Task<SingleProjectionState<TAggregateProjectionPayload>?>
        GetProjectionAsync<TAggregatePayload, TSingleProjection, TAggregateProjectionPayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload
    {
        var aggregate = await _singleProjection
            .GetAggregateAsync<TSingleProjection, SingleProjectionState<TAggregateProjectionPayload>,
                TSingleProjection>(aggregateId, toVersion);
        return aggregate?.ToState();
    }
    public async Task<Aggregate<TAggregatePayload>?> GetAggregateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new() => await _singleProjection
        .GetAggregateAsync<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>(
            aggregateId,
            toVersion);
    public async Task<AggregateState<TAggregatePayload>?> GetAggregateStateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var aggregate = await GetAggregateAsync<TAggregatePayload>(aggregateId, toVersion);
        return aggregate?.ToState();
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
    private async Task<Q?> GetAggregateStateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : ISingleAggregate, ISingleProjection, ISingleProjectionStateConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleProjector<T>, new()
    {
        var aggregate = await _singleProjection.GetAggregateAsync<T, Q, P>(aggregateId, toVersion);
        return aggregate is null ? default : aggregate.ToState();
    }
}
