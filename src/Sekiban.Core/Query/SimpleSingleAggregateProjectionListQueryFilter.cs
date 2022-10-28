using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query;

public class SimpleSingleAggregateProjectionListQueryFilter<TAggregate, TProjection, TAggregateProjectionPayload> :
    ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TProjection, TAggregateProjectionPayload,
        SimpleSingleAggregateProjectionListQueryFilter<TAggregate, TProjection, TAggregateProjectionPayload>.QueryParameter,
        SingleAggregateProjectionState<TAggregateProjectionPayload>>
    where TProjection : SingleAggregateProjectionBase<TAggregate, TProjection, TAggregateProjectionPayload>, new()
    where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
    where TAggregate : IAggregatePayload, new()
{
    public IEnumerable<SingleAggregateProjectionState<TAggregateProjectionPayload>> HandleFilter(
        QueryParameter queryParam,
        IEnumerable<SingleAggregateProjectionState<TAggregateProjectionPayload>> list)
    {
        return list;
    }
    public IEnumerable<SingleAggregateProjectionState<TAggregateProjectionPayload>> HandleSort(
        QueryParameter queryParam,
        IEnumerable<SingleAggregateProjectionState<TAggregateProjectionPayload>> projections)
    {
        return projections.OrderByDescending(m => m.LastSortableUniqueId);
    }
    public record QueryParameter(int? PageSize, int? PageNumber) : IQueryFilterParameter;
}
