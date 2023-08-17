using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
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
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjectionPayload>(
        string rootPartitionKey = ProjectionAllRootPartitions,
        SortableUniqueIdValue? includesSortableUniqueIdValue = null) where TProjectionPayload : IMultiProjectionPayloadCommon;
    /// <summary>
    ///     Get Aggregate List Projection Object
    ///     Uses all root partitions.
    /// </summary>
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> GetAggregateListObject<TAggregatePayload>(
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TAggregatePayload : IAggregatePayloadCommon =>
        GetAggregateListObject<TAggregatePayload>(ProjectionAllRootPartitions, includesSortableUniqueIdValue);
    /// <summary>
    ///     Get Aggregate List Projection Object
    ///     Specify root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> GetAggregateListObject<TAggregatePayload>(
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TAggregatePayload : IAggregatePayloadCommonBase;

    /// <summary>
    ///     Get Aggregate List by Multi Projection
    /// </summary>
    /// <param name="queryListType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = ProjectionAllRootPartitions,
        SortableUniqueIdValue? includesSortableUniqueIdValue = null) where TAggregatePayload : IAggregatePayloadCommonBase;
    /// <summary>
    ///     Get Single Projection List Object
    ///     Uses all root partitions.
    /// </summary>
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TSingleProjectionPayload>(SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        GetSingleProjectionListObject<TSingleProjectionPayload>(ProjectionAllRootPartitions, includesSortableUniqueIdValue);
    /// <summary>
    ///     Get Single Projection List Object
    ///     Specified root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TSingleProjectionPayload>(string rootPartitionKey, SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    /// <summary>
    ///     Get Single Projection List by Multi Projection
    /// </summary>
    /// <param name="queryListType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<List<SingleProjectionState<TSingleProjectionPayload>>> GetSingleProjectionList<TSingleProjectionPayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = ProjectionAllRootPartitions,
        SortableUniqueIdValue? includesSortableUniqueIdValue = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
}
