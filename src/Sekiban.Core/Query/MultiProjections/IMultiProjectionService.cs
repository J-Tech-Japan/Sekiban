using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjectionService
{
    public const string ProjectionAllPartitions = "";
    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjectionPayload>(
        string rootPartitionKey = ProjectionAllPartitions,
        SortableUniqueIdValue? includesSortableUniqueIdValue = null) where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    public Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> GetAggregateListObject<TAggregatePayload>(
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TAggregatePayload : IAggregatePayloadCommon =>
        GetAggregateListObject<TAggregatePayload>(ProjectionAllPartitions, includesSortableUniqueIdValue);

    public Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> GetAggregateListObject<TAggregatePayload>(
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TAggregatePayload : IAggregatePayloadCommon;

    public Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = ProjectionAllPartitions,
        SortableUniqueIdValue? includesSortableUniqueIdValue = null) where TAggregatePayload : IAggregatePayloadCommon;

    public Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TSingleProjectionPayload>(SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() =>
        GetSingleProjectionListObject<TSingleProjectionPayload>(ProjectionAllPartitions, includesSortableUniqueIdValue);

    public Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TSingleProjectionPayload>(string rootPartitionKey, SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();

    public Task<List<SingleProjectionState<TSingleProjectionPayload>>> GetSingleProjectionList<TSingleProjectionPayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = ProjectionAllPartitions,
        SortableUniqueIdValue? includesSortableUniqueIdValue = null) where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();
}
