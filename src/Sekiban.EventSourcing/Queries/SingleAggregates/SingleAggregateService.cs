using Sekiban.EventSourcing.Queries.SingleAggregates.SingleProjection;
namespace Sekiban.EventSourcing.Queries.SingleAggregates
{
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
        public async Task<T?> GetAggregateFromInitialAsync<T, P>(Guid aggregateId, int? toVersion) where T : ISingleAggregate, ISingleAggregateProjection
            where P : ISingleAggregateProjector<T>, new() =>
            await _singleAggregateFromInitial.GetAggregateFromInitialAsync<T, P>(aggregateId, toVersion);

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
        public async Task<T?> GetAggregateFromInitialDefaultAggregateAsync<T, TContents>(Guid aggregateId, int? toVersion = null)
            where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new() =>
            await GetAggregateFromInitialAsync<T, DefaultSingleAggregateProjector<T>>(aggregateId, toVersion);

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
        public async Task<AggregateDto<TContents>?> GetAggregateFromInitialDefaultAggregateDtoAsync<T, TContents>(Guid aggregateId, int? toVersion = null)
            where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new() =>
            (await GetAggregateFromInitialAsync<T, DefaultSingleAggregateProjector<T>>(aggregateId, toVersion))?.ToDto();

        public async Task<T?> GetProjectionAsync<T>(Guid aggregateId, int? toVersion = null) where T : SingleAggregateProjectionBase<T>, new() =>
            await GetAggregateDtoAsync<T, T, T>(aggregateId, toVersion);

        /// <summary>
        ///     スナップショット、メモリキャッシュを使用する通常版
        ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
        /// </summary>
        /// <param name="aggregateId"></param>
        /// <param name="toVersion"></param>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TContents"></typeparam>
        /// <returns></returns>
        public async Task<T?> GetAggregateAsync<T, TContents>(Guid aggregateId, int? toVersion = null) where T : TransferableAggregateBase<TContents>
            where TContents : IAggregateContents, new() =>
            await _singleProjection.GetAggregateAsync<T, AggregateDto<TContents>, DefaultSingleAggregateProjector<T>>(aggregateId, toVersion);
        /// <summary>
        ///     スナップショット、メモリキャッシュを使用する通常版
        ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
        /// </summary>
        /// <param name="aggregateId"></param>
        /// <param name="toVersion"></param>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TContents"></typeparam>
        /// <returns></returns>
        public async Task<AggregateDto<TContents>?> GetAggregateDtoAsync<T, TContents>(Guid aggregateId, int? toVersion = null)
            where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new()
        {
            var aggregate = await _singleProjection.GetAggregateAsync<T, AggregateDto<TContents>, DefaultSingleAggregateProjector<T>>(
                aggregateId,
                toVersion);
            return aggregate?.ToDto();
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
        private async Task<Q?> GetAggregateDtoAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
            where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
            where Q : ISingleAggregate
            where P : ISingleAggregateProjector<T>, new()
        {
            var aggregate = await _singleProjection.GetAggregateAsync<T, Q, P>(aggregateId, toVersion);
            return aggregate is null ? default : aggregate.ToDto();
        }
    }
}
