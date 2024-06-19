using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     Multi Projection State Retrieve Service
/// </summary>
public interface IMultiProjectionService
{
    /// <summary>
    ///     Const for using all root partitions.
    /// </summary>
    public const string ProjectionAllRootPartitions = "";
    /// <summary>
    ///     Get Multi Projection State
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="retrievalOptions"></param>
    /// <typeparam name="TProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjectionPayload>(
        string rootPartitionKey = ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TProjectionPayload : IMultiProjectionPayloadCommon;
    public Task<ResultBox<MultiProjectionState<TProjectionPayload>>> GetMultiProjectionWithResultAsync<TProjectionPayload>(
        string rootPartitionKey = ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TProjectionPayload : IMultiProjectionPayloadCommon =>
        ResultBox.WrapTry(() => GetMultiProjectionAsync<TProjectionPayload>(rootPartitionKey, retrievalOptions));
    /// <summary>
    ///     Get Aggregate List Projection Object
    ///     Uses all root partitions.
    /// </summary>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <param name="retrievalOptions"></param>
    /// <returns></returns>
    public Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> GetAggregateListObject<TAggregatePayload>(
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon =>
        GetAggregateListObject<TAggregatePayload>(ProjectionAllRootPartitions, retrievalOptions);
    /// <summary>
    ///     Get Aggregate List Projection Object
    ///     Specify root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="retrievalOptions"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> GetAggregateListObject<TAggregatePayload>(
        string rootPartitionKey,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon;

    /// <summary>
    ///     Get Aggregate List by Multi Projection
    /// </summary>
    /// <param name="queryListType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="retrievalOptions"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon;
    public Task<ResultBox<List<AggregateState<TAggregatePayload>>>> GetAggregateListWithResult<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon =>
        ResultBox.WrapTry(() => GetAggregateList<TAggregatePayload>(queryListType, rootPartitionKey, retrievalOptions));

    /// <summary>
    ///     Get Single Projection List Object
    ///     Uses all root partitions.
    /// </summary>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TSingleProjectionPayload>(MultiProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        GetSingleProjectionListObject<TSingleProjectionPayload>(ProjectionAllRootPartitions, retrievalOptions);
    /// <summary>
    ///     Get Single Projection List Object
    ///     Specified root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="retrievalOptions"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TSingleProjectionPayload>(string rootPartitionKey, MultiProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    /// <summary>
    ///     Get Single Projection List by Multi Projection
    /// </summary>
    /// <param name="queryListType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="retrievalOptions"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<List<SingleProjectionState<TSingleProjectionPayload>>> GetSingleProjectionList<TSingleProjectionPayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public Task<ResultBox<List<SingleProjectionState<TSingleProjectionPayload>>>> GetSingleProjectionListWithResult<TSingleProjectionPayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        ResultBox.WrapTry(() => GetSingleProjectionList<TSingleProjectionPayload>(queryListType, rootPartitionKey, retrievalOptions));
}
