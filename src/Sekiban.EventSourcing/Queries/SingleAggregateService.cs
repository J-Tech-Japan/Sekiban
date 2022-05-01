using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Partitions.AggregateIdPartitions;
using Sekiban.EventSourcing.Shared.Exceptions;
namespace Sekiban.EventSourcing.Queries;

public class SingleAggregateService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly ISingleAggregateProjectionQueryStore _singleAggregateProjectionQueryStore;

    public SingleAggregateService(
        ISingleAggregateProjectionQueryStore singleAggregateProjectionQueryStore,
        IDocumentRepository documentRepository)
    {
        _singleAggregateProjectionQueryStore = singleAggregateProjectionQueryStore;
        _documentRepository = documentRepository;
    }

    private async Task<SingleAggregateList<T>> GetAggregateListObjectAsync<T, Q, P>()
        where T : ISingleAggregate, ISingleAggregateProjection
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        var projector = new P();
        var aggregateList =
            _singleAggregateProjectionQueryStore.FindAggregateList<T>();
        if (aggregateList != null)
        {
            var allAfterEvents =
                await _documentRepository.GetAllAggregateEventsForAggregateEventTypeAsync(
                    projector.OriginalAggregateType(),
                    aggregateList.LastEventId); // 効率悪いけどこれで取れる
            return HandleListEvent(
                    projector,
                    allAfterEvents,
                    aggregateList) ??
                new SingleAggregateList<T>();
        }

        var allEvents =
            await _documentRepository.GetAllAggregateEventsForAggregateEventTypeAsync(
                projector.OriginalAggregateType(),
                aggregateList?.LastEventId);
        return HandleListEvent(projector, allEvents, aggregateList) ??
            new SingleAggregateList<T>();
    }

    private SingleAggregateList<T>? HandleListEvent<T>(
        ISingleAggregateProjector<T> projector,
        IEnumerable<AggregateEvent> events,
        SingleAggregateList<T>? aggregateList)
        where T : ISingleAggregate, ISingleAggregateProjection
    {
        var domainEvents = events.ToList();
        if (!domainEvents.Any())
        {
            return aggregateList;
        }
        var list = aggregateList?.List == null ? new List<T>() : new List<T>(aggregateList.List);
        foreach (var e in domainEvents.Where(
            m => m.AggregateType == projector.OriginalAggregateType().Name))
        {
            if (e.IsAggregateInitialEvent)
            {
                var aggregate = projector.CreateInitialAggregate(e.AggregateId);
                aggregate.ApplyEvent(e);
                list.Add(aggregate);
                continue;
            }
            var targetAggregate = list.FirstOrDefault(m => m.AggregateId == e.AggregateId);
            if (targetAggregate == null)
            {
                continue;
            }
            targetAggregate.ApplyEvent(e);
        }
        if (aggregateList == null)
        {
            aggregateList = new SingleAggregateList<T>
            {
                List = list,
                LastEventId = domainEvents.Last().Id
            };
        }
        else
        {
            aggregateList.List = list;
            aggregateList.LastEventId = domainEvents.Last().Id;
        }
        _singleAggregateProjectionQueryStore.SaveLatestAggregateList(aggregateList);
        return aggregateList;
    }
    private async Task<IEnumerable<T>> ListAsync<T, Q, P>(QueryListType queryListType)
        where T : ISingleAggregate, ISingleAggregateProjection
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        var aggregateList = await GetAggregateListObjectAsync<T, Q, P>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => aggregateList.List,
            QueryListType.ActiveOnly => aggregateList.List.Where(m => m.IsDeleted == false),
            QueryListType.DeletedOnly => aggregateList.List.Where(m => m.IsDeleted),
            _ => throw new JJInvalidArgumentException()
        };
    }
    public async Task<IEnumerable<T>> ListAsync<T, Q>(QueryListType queryListType)
        where T : TransferableAggregateBase<Q>
        where Q : AggregateDtoBase =>
        await ListAsync<T, Q, DefaultSingleAggregateProjector<T>>(queryListType);
    private async Task<IEnumerable<Q>> DtoListAsync<T, Q, P>(
        QueryListType queryListType = QueryListType.ActiveOnly)
        where T : ISingleAggregate, ISingleAggregateProjection,
        ISingleAggregateProjectionDtoConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        return (await ListAsync<T, Q, P>(queryListType)).Select(
            m => m.ToDto());
    }
    public async Task<IEnumerable<T>> DtoListAsync<T>(
        QueryListType queryListType = QueryListType.ActiveOnly)
        where T : SingleAggregateProjectionBase<T>, new()
    {
        return (await ListAsync<T, T, T>(queryListType)).Select(
            m => m.ToDto());
    }
    public async Task<IEnumerable<Q>> DtoListAsync<T, Q>(
        QueryListType queryListType = QueryListType.ActiveOnly)
        where T : TransferableAggregateBase<Q>
        where Q : AggregateDtoBase
    {
        var projector = new DefaultSingleAggregateProjector<T>();
        return (await ListAsync<T, Q, DefaultSingleAggregateProjector<T>>(queryListType)).Select(
            m => m.ToDto());
    }

    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    public async Task<T?> GetAggregateFromInitialAsync<T, P>(
        Guid aggregateId)
        where T : ISingleAggregate, ISingleAggregateProjection
        where P : ISingleAggregateProjector<T>, new()
    {
        var projector = new P();
        var allEvents = await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
            aggregateId,
            typeof(T),
            new AggregateIdPartitionKeyFactory(aggregateId, projector.OriginalAggregateType())
                .GetPartitionKey(
                    DocumentType.AggregateEvent));
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        foreach (var e in allEvents) { aggregate.ApplyEvent(e); }
        _singleAggregateProjectionQueryStore.SaveProjection(aggregate, typeof(T).Name);
        return aggregate;
    }
    /// <summary>
    ///     メモリキャッシュも使用せず、初期イベントからAggregateを作成します。
    ///     遅いので、通常はキャッシュバージョンを使用ください
    ///     検証などのためにこちらを残しています。
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    public async Task<Q?> GetAggregateFromInitialDtoAsync<T, Q, P>(
        Guid aggregateId)
        where T : ISingleAggregate, ISingleAggregateProjection,
        ISingleAggregateProjectionDtoConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        var aggregate = await GetAggregateFromInitialAsync<T, P>(aggregateId);
        var projector = new P();
        return aggregate != null ? aggregate.ToDto() : default;
    }

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <typeparam name="P"></typeparam>
    /// <returns></returns>
    private async Task<T?> GetAggregateAsync<T, Q, P>(
        Guid aggregateId)
        where T : ISingleAggregate, ISingleAggregateProjection
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        var fromStore =
            _singleAggregateProjectionQueryStore.FindAggregate<T>(
                aggregateId,
                typeof(T).Name);
        var projector = new P();
        if (fromStore != null)
        {
            var allAfterEvents = await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
                aggregateId,
                typeof(T),
                new AggregateIdPartitionKeyFactory(aggregateId, projector.OriginalAggregateType())
                    .GetPartitionKey(
                        DocumentType.AggregateEvent),
                fromStore.LastEventId);
            foreach (var e in allAfterEvents) { fromStore.ApplyEvent(e); }
            return fromStore;
        }
        var allEvents = await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
            aggregateId,
            typeof(T),
            new AggregateIdPartitionKeyFactory(aggregateId, projector.OriginalAggregateType())
                .GetPartitionKey(
                    DocumentType.AggregateEvent));
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        foreach (var e in allEvents) { aggregate.ApplyEvent(e); }
        _singleAggregateProjectionQueryStore.SaveProjection(aggregate, typeof(T).Name);
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
    private async Task<Q?> GetAggregateDtoAsync<T, Q, P>(
        Guid aggregateId)
        where T : ISingleAggregate, ISingleAggregateProjection,
        ISingleAggregateProjectionDtoConvertible<Q>
        where Q : ISingleAggregate
        where P : ISingleAggregateProjector<T>, new()
    {
        var aggregate =
            await GetAggregateAsync<T, Q, P>(aggregateId);
        return aggregate == null ? default : aggregate.ToDto();
    }

    public async Task<T?> GetProjectionAsync<T>(Guid aggregateId)
        where T : SingleAggregateProjectionBase<T>, new() =>
        await GetAggregateDtoAsync<T, T, T>(aggregateId);

    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <returns></returns>
    public async Task<T?> GetAggregateAsync<T, Q>(
        Guid aggregateId)
        where T : TransferableAggregateBase<Q>
        where Q : AggregateDtoBase =>
        await GetAggregateAsync<T, Q, DefaultSingleAggregateProjector<T>>(
            aggregateId);
    /// <summary>
    ///     スナップショット、メモリキャッシュを使用する通常版
    ///     こちらはデフォルトプロジェクトション（集約のデフォルトステータス）
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="Q"></typeparam>
    /// <returns></returns>
    public async Task<Q?> GetAggregateDtoAsync<T, Q>(
        Guid aggregateId)
        where T : TransferableAggregateBase<Q>
        where Q : AggregateDtoBase
    {
        var aggregate =
            await GetAggregateAsync<T, Q, DefaultSingleAggregateProjector<T>>(
                aggregateId);
        return aggregate?.ToDto();
    }
}
