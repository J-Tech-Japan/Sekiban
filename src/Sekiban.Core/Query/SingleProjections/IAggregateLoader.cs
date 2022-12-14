using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
namespace Sekiban.Core.Query.SingleProjections;

public interface IAggregateLoader
{
    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateState<TAggregatePayload>?> AsDefaultStateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null)
        where TAggregatePayload : IAggregatePayload, new();

    /// <summary>
    ///     カスタムプロジェククションを取得
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<SingleProjectionState<TSingleProjectionPayload>?>
        AsSingleProjectionStateAsync<TSingleProjectionPayload>(Guid aggregateId, int? toVersion = null, string? includesSortableUniqueId = null)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();


    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<Aggregate<TAggregatePayload>?> AsAggregateAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
        where TAggregatePayload : IAggregatePayload, new();

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateState<TAggregatePayload>?> AsDefaultStateAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
        where TAggregatePayload : IAggregatePayload, new();


    public Task<IEnumerable<IEvent>?> AllEventsAsync<TAggregatePayload>(
        Guid aggregateId,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
        where TAggregatePayload : IAggregatePayload, new();
}
