using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query;

public class SimpleAggregateListQueryFilter<TAggregate> : IAggregateListQueryFilterDefinition<TAggregate,
    SimpleAggregateListQueryFilter<TAggregate>.QueryParameter,
    AggregateState<TAggregate>> where TAggregate : IAggregatePayload, new()
{
    public IEnumerable<AggregateState<TAggregate>> HandleFilter(QueryParameter queryParam, IEnumerable<AggregateState<TAggregate>> list)
    {
        return list;
    }
    public IEnumerable<AggregateState<TAggregate>> HandleSort(QueryParameter queryParam, IEnumerable<AggregateState<TAggregate>> projections)
    {
        return projections.OrderByDescending(m => m.LastSortableUniqueId);
    }
    public record QueryParameter(int? PageSize, int? PageNumber) : IQueryFilterParameter;
}
