using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query;

public class SimpleSingleProjectionListQuery<TAggregatePayload, TProjection, TSingleProjectionPayload> :
    ISingleProjectionListQuery<TAggregatePayload, TProjection, TSingleProjectionPayload,
        SimpleSingleProjectionListQuery<TAggregatePayload, TProjection, TSingleProjectionPayload>.QueryParameter,
        SingleProjectionState<TSingleProjectionPayload>>
    where TProjection : SingleProjectionBase<TAggregatePayload, TProjection, TSingleProjectionPayload>, new()
    where TSingleProjectionPayload : ISingleProjectionPayload
    where TAggregatePayload : IAggregatePayload, new()
{
    public IEnumerable<SingleProjectionState<TSingleProjectionPayload>> HandleFilter(
        QueryParameter queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> list) => list;
    public IEnumerable<SingleProjectionState<TSingleProjectionPayload>> HandleSort(
        QueryParameter queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections)
    {
        return projections.OrderByDescending(m => m.LastSortableUniqueId);
    }
    public record QueryParameter(int? PageSize, int? PageNumber) : IQueryPagingParameter;
}
