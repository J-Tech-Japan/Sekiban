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
    /// <typeparam name="TContents"></typeparam>
    /// <returns></returns>
    public Task<T?> GetAggregateFromInitialDefaultAggregateAsync<T, TContents>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new();

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TContents"></typeparam>
    /// <returns></returns>
    public Task<AggregateDto<TContents>?> GetAggregateFromInitialDefaultAggregateDtoAsync<T, TContents>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new();
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
    /// <typeparam name="TContents"></typeparam>
    /// <returns></returns>
    public Task<T?> GetAggregateAsync<T, TContents>(Guid aggregateId, int? toVersion = null) where T : TransferableAggregateBase<TContents>
        where TContents : IAggregateContents, new();

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TContents"></typeparam>
    /// <returns></returns>
    public Task<AggregateDto<TContents>?> GetAggregateDtoAsync<T, TContents>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new();
}
