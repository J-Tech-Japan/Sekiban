namespace Sekiban.EventSourcing.Queries.SingleAggregates.SingleProjection
{
    public interface ISingleAggregateFromInitial
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
        Task<T?> GetAggregateFromInitialAsync<T, P>(Guid aggregateId, int? toVersion) where T : ISingleAggregate, ISingleAggregateProjection
            where P : ISingleAggregateProjector<T>, new();
    }
}
