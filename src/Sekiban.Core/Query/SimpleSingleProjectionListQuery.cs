using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query;

public record QuerySingleProjectionState<TSingleProjectionPayload>(SingleProjectionState<TSingleProjectionPayload> State) : IQueryResponse
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon;
public record SimpleSingleProjectionListQueryParameter<TSingleProjectionPayload>
    (int? PageSize, int? PageNumber) : IListQueryPagingParameter<QuerySingleProjectionState<TSingleProjectionPayload>>
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon;
public class SimpleSingleProjectionListQuery<TSingleProjectionPayload> : ISingleProjectionListQuery<TSingleProjectionPayload,
    SimpleSingleProjectionListQueryParameter<TSingleProjectionPayload>, QuerySingleProjectionState<TSingleProjectionPayload>>
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon
{
    public IEnumerable<QuerySingleProjectionState<TSingleProjectionPayload>> HandleFilter(
        SimpleSingleProjectionListQueryParameter<TSingleProjectionPayload> queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> list) =>
        list.Select(m => new QuerySingleProjectionState<TSingleProjectionPayload>(m));
    public IEnumerable<QuerySingleProjectionState<TSingleProjectionPayload>> HandleSort(
        SimpleSingleProjectionListQueryParameter<TSingleProjectionPayload> queryParam,
        IEnumerable<QuerySingleProjectionState<TSingleProjectionPayload>> filteredList) =>
        filteredList.OrderByDescending(m => m.State.LastSortableUniqueId);
}
