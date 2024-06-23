using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryContext
{
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class;
    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class;
    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>() where T1 : class where T2 : class where T3 : class;
    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class;
    public ResultBox<IMultiProjectionService> GetMultiProjectionService();

    public Task<ResultBox<MultiProjectionState<TProjectionPayload>>> GetMultiProjectionAsync<TProjectionPayload>(
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TProjectionPayload : IMultiProjectionPayloadCommon =>
        GetMultiProjectionService()
            .Conveyor(service => ResultBox.WrapTry(() => service.GetMultiProjectionAsync<TProjectionPayload>(rootPartitionKey, retrievalOptions)));
    public Task<ResultBox<List<SingleProjectionState<TSingleProjectionPayload>>>> GetSingleProjectionList<TSingleProjectionPayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        GetMultiProjectionService()
            .Conveyor(
                service => ResultBox.WrapTry(
                    () => service.GetSingleProjectionList<TSingleProjectionPayload>(queryListType, rootPartitionKey, retrievalOptions)));
    public Task<ResultBox<List<AggregateState<TAggregatePayload>>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon =>
        GetMultiProjectionService()
            .Conveyor(
                service => ResultBox.WrapTry(() => service.GetAggregateList<TAggregatePayload>(queryListType, rootPartitionKey, retrievalOptions)));
}
