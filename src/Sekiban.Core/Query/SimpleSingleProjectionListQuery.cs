using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query;

public class SimpleSingleProjectionListQuery<TAggregate, TProjection, TAggregateProjectionPayload> :
    ISingleProjectionListQuery<TAggregate, TProjection, TAggregateProjectionPayload,
        SimpleSingleProjectionListQuery<TAggregate, TProjection, TAggregateProjectionPayload>.QueryParameter,
        SingleProjectionState<TAggregateProjectionPayload>>
    where TProjection : SingleProjectionBase<TAggregate, TProjection, TAggregateProjectionPayload>, new()
    where TAggregateProjectionPayload : ISingleProjectionPayload
    where TAggregate : IAggregatePayload, new()
{
    public IEnumerable<SingleProjectionState<TAggregateProjectionPayload>> HandleFilter(
        QueryParameter queryParam,
        IEnumerable<SingleProjectionState<TAggregateProjectionPayload>> list) => list;
    public IEnumerable<SingleProjectionState<TAggregateProjectionPayload>> HandleSort(
        QueryParameter queryParam,
        IEnumerable<SingleProjectionState<TAggregateProjectionPayload>> projections)
    {
        return projections.OrderByDescending(m => m.LastSortableUniqueId);
    }
    public record QueryParameter(int? PageSize, int? PageNumber) : IQueryPagingParameter;
}
