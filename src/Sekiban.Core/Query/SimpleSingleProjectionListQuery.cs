using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;

namespace Sekiban.Core.Query;

public class SimpleSingleProjectionListQuery<TSingleProjectionPayload> :
    ISingleProjectionListQuery<TSingleProjectionPayload,
        SimpleSingleProjectionListQuery<TSingleProjectionPayload>.QueryParameter,
        SingleProjectionState<TSingleProjectionPayload>>
    where TSingleProjectionPayload : ISingleProjectionPayload
{
    public IEnumerable<SingleProjectionState<TSingleProjectionPayload>> HandleFilter(
        QueryParameter queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> list)
    {
        return list;
    }

    public IEnumerable<SingleProjectionState<TSingleProjectionPayload>> HandleSort(
        QueryParameter queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> filteredList)
    {
        return filteredList.OrderByDescending(m => m.LastSortableUniqueId);
    }

    public record QueryParameter(int? PageSize, int? PageNumber) : IQueryPagingParameter;
}
