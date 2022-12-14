using Sekiban.Core.Aggregate;
using Sekiban.Core.Document.ValueObjects;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjectionService
{
    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjectionPayload>(
        SortableUniqueIdValue? includesSortableUniqueIdValue = null)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    public Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>(SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TAggregatePayload : IAggregatePayload, new();

    public Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
        SortableUniqueIdValue? includesSortableUniqueIdValue = null,
        QueryListType queryListType = QueryListType.ActiveOnly)
        where TAggregatePayload : IAggregatePayload, new();

    public
        Task<MultiProjectionState<
            SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TSingleProjectionPayload>(SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();

    public Task<List<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionList<TSingleProjectionPayload>(
            SortableUniqueIdValue? includesSortableUniqueIdValue = null,
            QueryListType queryListType = QueryListType.ActiveOnly)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();
}
