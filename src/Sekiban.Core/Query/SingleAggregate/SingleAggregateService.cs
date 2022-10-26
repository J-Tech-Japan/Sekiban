using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleAggregate.SingleProjection;
namespace Sekiban.Core.Query.SingleAggregate;

public class SingleAggregateService : ISingleAggregateService
{
    private readonly ISingleAggregateFromInitial _singleAggregateFromInitial;
    private readonly ISingleProjection _singleProjection;
    public SingleAggregateService(ISingleProjection singleProjection, ISingleAggregateFromInitial singleAggregateFromInitial)
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
        where T : ISingleAggregate, ISingleAggregateProjection
        where P : ISingleAggregateProjector<T>, new()
    {
        return await _singleAggregateFromInitial.GetAggregateFromInitialAsync<T, P>(aggregateId, toVersion);
    }
    public Task<Aggregate<TAggregatePayload>?> GetAggregateFromInitialAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new()
    {
        return GetAggregateProjectionFromInitialAsync<Aggregate<TAggregatePayload>, DefaultSingleAggregateProjector<TAggregatePayload>>(
            aggregateId,
            toVersion);
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
    public async Task<AggregateState<TAggregatePayload>?> GetAggregateStateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null) where TAggregatePayload : IAggregatePayload, new()
    {
        var aggregate = await GetAggregateFromInitialAsync<TAggregatePayload>(aggregateId, toVersion);
        return aggregate?.ToState();
    }

    public async Task<SingleAggregateProjectionState<TAggregateProjectionPayload>?>
        GetProjectionAsync<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>(Guid aggregateId, int? toVersion = null)
        where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
    {
        var aggregate = await _singleProjection
            .GetAggregateAsync<TSingleAggregateProjection, SingleAggregateProjectionState<TAggregateProjectionPayload>,
                TSingleAggregateProjection>(aggregateId, toVersion);
        return aggregate?.ToState();
    }
    public async Task<Aggregate<TAggregatePayload>?> GetAggregateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new()
    {
        return await _singleProjection
            .GetAggregateAsync<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>, DefaultSingleAggregateProjector<TAggregatePayload>>(
                aggregateId,
                toVersion);
    }
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
        where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionStateConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        var aggregate = await _singleProjection.GetAggregateAsync<T, Q, P>(aggregateId, toVersion);
        return aggregate is null ? default : aggregate.ToState();
    }
}
