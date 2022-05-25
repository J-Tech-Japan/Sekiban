namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public interface ISingleAggregateService
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
    public Task<T?> GetAggregateFromInitialAsync<T, P>(Guid aggregateId, int? toVersion) where T : ISingleAggregate, ISingleAggregateProjection
        where P : ISingleAggregateProjector<T>, new();

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public Task<T?> GetAggregateFromInitialDefaultAggregateAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase;

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public Task<Q?> GetAggregateFromInitialDefaultAggregateDtoAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase;
    /// <summary>
    ///     カスタムプロジェククションを取得
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public Task<T?> GetProjectionAsync<T>(Guid aggregateId, int? toVersion = null) where T : SingleAggregateProjectionBase<T>, new();

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <returns></returns>
    public Task<T?> GetAggregateAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase;

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <returns></returns>
    public Task<Q?> GetAggregateDtoAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase;
}
