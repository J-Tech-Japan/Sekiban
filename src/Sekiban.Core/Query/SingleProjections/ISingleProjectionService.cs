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
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    public Task<T?> GetAggregateProjectionFromInitialAsync<T, P>(Guid aggregateId, int? toVersion)
        where T : IAggregateIdentifier, ISingleProjection
        where P : ISingleProjector<T>, new();

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
    public Task<AggregateIdentifier<TAggregatePayload>?> GetAggregateFromInitialAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
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
    public Task<AggregateIdentifierState<TAggregatePayload>?> GetAggregateStateFromInitialAsync<TAggregatePayload>(
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
        where TSingleProjection : MultiProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
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
    public Task<AggregateIdentifier<TAggregatePayload>?> GetAggregateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new();

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateIdentifierState<TAggregatePayload>?> GetAggregateStateAsync<TAggregatePayload>(Guid aggregateId, int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new();
}
