namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public class SingleAggregateService
{
    private readonly IDocumentRepository _documentRepository;

    public SingleAggregateService(IDocumentRepository documentRepository) =>
        _documentRepository = documentRepository;

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
        where P : ISingleAggregateProjector<T>, new()
    {
        var projector = new P();
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var addFinished = false;
        await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
            aggregateId,
            typeof(T),
            new AggregateIdPartitionKeyFactory(aggregateId, projector.OriginalAggregateType()).GetPartitionKey(DocumentType.AggregateEvent),
            null,
            events =>
            {
                if (events.Count() != events.Select(m => m.Id).Distinct().Count())
                {
                    throw new JJAggregateEventDuplicateException();
                }
                if (addFinished) { return; }
                foreach (var e in events)
                {
                    aggregate.ApplyEvent(e);
                    if (toVersion.HasValue && toVersion.Value == aggregate.Version)
                    {
                        addFinished = true;
                        break;
                    }
                }
            });
        return aggregate;
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
    public async Task<T?> GetAggregateFromInitialDefaultAggregateAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase =>
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
    public async Task<Q?> GetAggregateFromInitialDefaultAggregateDtoAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase =>
        (await GetAggregateFromInitialAsync<T, DefaultSingleAggregateProjector<T>>(aggregateId, toVersion))?.ToDto();

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    private async Task<T?> GetAggregateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        var projector = new P();
        var aggregate = projector.CreateInitialAggregate(aggregateId);

        var snapshotDocument = await _documentRepository.GetLatestSnapshotForAggregateAsync(aggregateId, typeof(T));
        var dto = snapshotDocument == null ? default : snapshotDocument.ToDto<Q>();
        if (dto != null)
        {
            aggregate.ApplySnapshot(dto);
        }
        if (toVersion.HasValue && aggregate.Version >= toVersion.Value)
        {
            return await GetAggregateFromInitialAsync<T, P>(aggregateId, toVersion.Value);
        }
        await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
            aggregateId,
            projector.OriginalAggregateType(),
            new AggregateIdPartitionKeyFactory(aggregateId, projector.OriginalAggregateType()).GetPartitionKey(DocumentType.AggregateEvent),
            dto?.LastSortableUniqueId,
            events =>
            {
                foreach (var e in events)
                {
                    if (!string.IsNullOrWhiteSpace(dto?.LastSortableUniqueId) &&
                        string.CompareOrdinal(dto?.LastSortableUniqueId, e.SortableUniqueId) > 0)
                    {
                        throw new JJAggregateEventDuplicateException();
                    }
                    aggregate.ApplyEvent(e);
                    if (toVersion.HasValue && aggregate.Version == toVersion.Value)
                    {
                        break;
                    }
                }
            });
        if (toVersion.HasValue && aggregate.Version < toVersion.Value)
        {
            throw new JJVersionNotReachToSpecificVersion();
        }
        return aggregate;
    }
    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    private async Task<Q?> GetAggregateDtoAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        var aggregate = await GetAggregateAsync<T, Q, P>(aggregateId, toVersion);
        return aggregate == null ? default : aggregate.ToDto();
    }

    public async Task<T?> GetProjectionAsync<T>(Guid aggregateId, int? toVersion = null) where T : SingleAggregateProjectionBase<T>, new() =>
        await GetAggregateDtoAsync<T, T, T>(aggregateId, toVersion);

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <returns></returns>
    public async Task<T?> GetAggregateAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase =>
        await GetAggregateAsync<T, Q, DefaultSingleAggregateProjector<T>>(aggregateId, toVersion);
    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <returns></returns>
    public async Task<Q?> GetAggregateDtoAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase
    {
        var aggregate = await GetAggregateAsync<T, Q, DefaultSingleAggregateProjector<T>>(aggregateId, toVersion);
        return aggregate?.ToDto();
    }
}