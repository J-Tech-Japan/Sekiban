using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionService
{
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
    public Task<TProjection?> GetAggregateProjectionFromInitialAsync<TProjection, TProjector>(Guid aggregateId, int? toVersion)
        where TProjection : IAggregateCommon, ISingleProjection
        where TProjector : ISingleProjector<TProjection>, new();

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<Aggregate<TAggregatePayload>?> GetAggregateFromInitialAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new();

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateState<TAggregatePayload>?> GetAggregateStateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new();
    /// <summary>
    ///     カスタムプロジェククションを取得
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <typeparam name="TSingleProjection"></typeparam>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<SingleProjectionState<TSingleProjectionPayload>?>
        GetProjectionAsync<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload;


    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<Aggregate<TAggregatePayload>?> GetAggregateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new();

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateState<TAggregatePayload>?> GetAggregateStateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new();
}
