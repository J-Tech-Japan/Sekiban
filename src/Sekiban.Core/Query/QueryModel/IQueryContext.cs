using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryContext
{
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class;
    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class;

    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>()
        where T1 : class where T2 : class where T3 : class;

    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class;

    public ResultBox<IMultiProjectionService> GetMultiProjectionService();

    public Task<ResultBox<MultiProjectionState<TProjectionPayload>>> GetMultiProjectionAsync<TProjectionPayload>(
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null)
        where TProjectionPayload : IMultiProjectionPayloadCommon =>
        GetMultiProjectionService()
            .Conveyor(
                service => ResultBox.WrapTry(
                    () => service.GetMultiProjectionAsync<TProjectionPayload>(rootPartitionKey, retrievalOptions)));

    public Task<ResultBox<List<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionList<TSingleProjectionPayload>(
            QueryListType queryListType = QueryListType.ActiveOnly,
            string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
            MultiProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        GetMultiProjectionService()
            .Conveyor(
                service => ResultBox.WrapTry(
                    () => service.GetSingleProjectionList<TSingleProjectionPayload>(
                        queryListType,
                        rootPartitionKey,
                        retrievalOptions)));

    public Task<ResultBox<List<AggregateState<TAggregatePayload>>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon =>
        GetMultiProjectionService()
            .Conveyor(
                service => ResultBox.WrapTry(
                    () => service.GetAggregateList<TAggregatePayload>(
                        queryListType,
                        rootPartitionKey,
                        retrievalOptions)));

    public Task<ResultBox<AggregateState<TAnotherAggregatePayload>>> GetAggregateState<TAnotherAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TAnotherAggregatePayload : IAggregatePayloadCommon =>
        GetRequiredService<IAggregateLoader>()
            .Conveyor(
                executor => executor.AsDefaultStateWithResultAsync<TAnotherAggregatePayload>(
                    aggregateId,
                    rootPartitionKey,
                    toVersion));

    public Task<ResultBox<AggregateState<TAnotherAggregatePayload>>>
        GetAggregateStateFromInitial<TAnotherAggregatePayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TAnotherAggregatePayload : IAggregatePayloadCommon =>
        GetRequiredService<IAggregateLoader>()
            .Conveyor(
                loader => loader.AsDefaultStateFromInitialWithResultAsync<TAnotherAggregatePayload>(
                    aggregateId,
                    rootPartitionKey,
                    toVersion));

    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        GetRequiredService<IAggregateLoader>()
            .Conveyor(
                loader => ResultBox.CheckNullWrapTry(
                    () => loader.AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
                        aggregateId,
                        rootPartitionKey,
                        toVersion)));

    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionStateAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null,
            SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        GetRequiredService<IAggregateLoader>()
            .Conveyor(
                loader => ResultBox.CheckNullWrapTry(
                    () => loader.AsSingleProjectionStateAsync<TSingleProjectionPayload>(
                        aggregateId,
                        rootPartitionKey,
                        toVersion)));

    public Task<ResultBox<TOutput>> ExecuteQueryAsync<TOutput>(INextQueryCommon<TOutput> query)
        where TOutput : notnull =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteAsync(query));

    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQueryAsync<TOutput>(INextListQueryCommon<TOutput> query)
        where TOutput : notnull =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteAsync(query));

    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQueryAsync<TOutput>(IListQueryInput<TOutput> param)
        where TOutput : IQueryResponse =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteWithResultAsync(param));

    public Task<ResultBox<TOutput>> ExecuteQueryAsync<TOutput>(IQueryInput<TOutput> param)
        where TOutput : IQueryResponse =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteWithResultAsync(param));
}
