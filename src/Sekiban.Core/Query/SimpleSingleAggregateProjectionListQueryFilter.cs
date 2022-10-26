using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query;

public class SimpleSingleAggregateProjectionListQueryFilter<TAggregate, TProjection, TSingleAggregateContents> :
    ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TProjection, TSingleAggregateContents,
        SimpleSingleAggregateProjectionListQueryFilter<TAggregate, TProjection, TSingleAggregateContents>.QueryParameter,
        SingleAggregateProjectionDto<TSingleAggregateContents>>
    where TProjection : SingleAggregateProjectionBase<TAggregate, TProjection, TSingleAggregateContents>
    where TSingleAggregateContents : ISingleAggregateProjectionPayload
    where TAggregate : IAggregatePayload, new()
{
    public IEnumerable<SingleAggregateProjectionDto<TSingleAggregateContents>> HandleFilter(
        QueryParameter queryParam,
        IEnumerable<SingleAggregateProjectionDto<TSingleAggregateContents>> list)
    {
        return list;
    }
    public IEnumerable<SingleAggregateProjectionDto<TSingleAggregateContents>> HandleSort(
        QueryParameter queryParam,
        IEnumerable<SingleAggregateProjectionDto<TSingleAggregateContents>> projections)
    {
        return projections.OrderByDescending(m => m.LastSortableUniqueId);
    }
    public record QueryParameter(int? PageSize, int? PageNumber) : IQueryFilterParameter;
}
