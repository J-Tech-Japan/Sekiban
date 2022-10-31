using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query;

public class SimpleAggregateListQuery<TAggregate> : IAggregateListQuery<TAggregate,
    SimpleAggregateListQuery<TAggregate>.QueryParameter,
    AggregateState<TAggregate>> where TAggregate : IAggregatePayload, new()
{
    public IEnumerable<AggregateState<TAggregate>> HandleFilter(QueryParameter queryParam, IEnumerable<AggregateState<TAggregate>> list) => list;
    public IEnumerable<AggregateState<TAggregate>> HandleSort(QueryParameter queryParam, IEnumerable<AggregateState<TAggregate>> projections)
    {
        return projections.OrderByDescending(m => m.LastSortableUniqueId);
    }
    public record QueryParameter(int? PageSize, int? PageNumber) : IQueryPagingParameter;
}
