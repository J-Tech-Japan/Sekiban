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
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    public async Task<T?> GetAggregateProjectionFromInitialAsync<T, P>(Guid aggregateId, int? toVersion)
        where T : IAggregateIdentifier, ISingleProjection
        where P : ISingleProjector<T>, new() => await singleProjectionFromInitial.GetAggregateFromInitialAsync<T, P>(aggregateId, toVersion);
    public Task<AggregateIdentifier<TAggregatePayload>?> GetAggregateFromInitialAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new() =>
        GetAggregateProjectionFromInitialAsync<AggregateIdentifier<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>(
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
    public async Task<AggregateIdentifierState<TAggregatePayload>?> GetAggregateStateFromInitialAsync<TAggregatePayload>(
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
    public async Task<AggregateIdentifier<TAggregatePayload>?> GetAggregateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new() => await _singleProjection
        .GetAggregateAsync<AggregateIdentifier<TAggregatePayload>, AggregateIdentifierState<TAggregatePayload>,
            DefaultSingleProjector<TAggregatePayload>>(
            aggregateId,
            toVersion);
    public async Task<AggregateIdentifierState<TAggregatePayload>?> GetAggregateStateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
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
        where T : IAggregateIdentifier, ISingleProjection, ISingleProjectionStateConvertible<Q>
        where Q : IAggregateIdentifier
        where P : ISingleProjector<T>, new()
    {
        var aggregate = await _singleProjection.GetAggregateAsync<T, Q, P>(aggregateId, toVersion);
        return aggregate is null ? default : aggregate.ToState();
    }
}
