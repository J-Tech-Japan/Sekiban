namespace Sekiban.Core.Query.SingleProjections.Projections;

public interface ISingleProjectionFromInitial
{
    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjector"></typeparam>
    /// <returns></returns>
    Task<TProjection?> GetAggregateFromInitialAsync<TProjection, TProjector>(
        Guid aggregateId,
        int? toVersion)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection
        where TProjector : ISingleProjector<TProjection>, new();
}
