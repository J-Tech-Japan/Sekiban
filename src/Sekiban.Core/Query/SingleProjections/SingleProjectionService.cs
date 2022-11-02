using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections.Projections;
namespace Sekiban.Core.Query.SingleProjections;

public class SingleProjectionService : ISingleProjectionService
{
    private readonly Projections.ISingleProjection _singleProjection;
    private readonly ISingleProjectionFromInitial singleProjectionFromInitial;
    public SingleProjectionService(Projections.ISingleProjection singleProjection, ISingleProjectionFromInitial singleProjectionFromInitial)
    {
        _singleProjection = singleProjection;
        this.singleProjectionFromInitial = singleProjectionFromInitial;
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
    public async Task<TProjection?> GetAggregateProjectionFromInitialAsync<TProjection, TProjector>(Guid aggregateId, int? toVersion)
        where TProjection : IAggregateCommon, ISingleProjection
        where TProjector : ISingleProjector<TProjection>, new() => await singleProjectionFromInitial.GetAggregateFromInitialAsync<TProjection, TProjector>(aggregateId, toVersion);
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

    public async Task<SingleProjectionState<TSingleProjectionPayload>?>
        GetProjectionAsync<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : MultiProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload
    {
        var aggregate = await _singleProjection
            .GetAggregateAsync<TSingleProjection, SingleProjectionState<TSingleProjectionPayload>,
                TSingleProjection>(aggregateId, toVersion);
        return aggregate?.ToState();
    }
    public async Task<Aggregate<TAggregatePayload>?> GetAggregateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new() => await _singleProjection
        .GetAggregateAsync<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>,
            DefaultSingleProjector<TAggregatePayload>>(
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
    /// <typeparam name="TAggregate"></typeparam>
    /// <typeparam name="TAggregateState"></typeparam>
    /// <typeparam name="TSingleProjector"></typeparam>
    /// <returns></returns>
    private async Task<TAggregateState?> GetAggregateStateAsync<TAggregate, TAggregateState, TSingleProjector>(
        Guid aggregateId,
        int? toVersion = null)
        where TAggregate : IAggregateCommon, ISingleProjection, ISingleProjectionStateConvertible<TAggregateState>
        where TAggregateState : IAggregateCommon
        where TSingleProjector : ISingleProjector<TAggregate>, new()
    {
        var aggregate = await _singleProjection.GetAggregateAsync<TAggregate, TAggregateState, TSingleProjector>(aggregateId, toVersion);
        return aggregate is null ? default : aggregate.ToState();
    }
}
