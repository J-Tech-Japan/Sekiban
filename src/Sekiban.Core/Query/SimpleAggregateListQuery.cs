using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query;

public class SimpleAggregateListQuery<TAggregatePayload> : IAggregateListQuery<TAggregatePayload,
    SimpleAggregateListQuery<TAggregatePayload>.QueryParameter,
    AggregateState<TAggregatePayload>> where TAggregatePayload : IAggregatePayload, new()
{
    public IEnumerable<AggregateState<TAggregatePayload>> HandleFilter(
        QueryParameter queryParam,
        IEnumerable<AggregateState<TAggregatePayload>> list) => list;
    public IEnumerable<AggregateState<TAggregatePayload>> HandleSort(
        QueryParameter queryParam,
        IEnumerable<AggregateState<TAggregatePayload>> filteredList)
    {
        return filteredList.OrderByDescending(m => m.LastSortableUniqueId);
    }
    public record QueryParameter(int? PageSize, int? PageNumber) : IQueryPagingParameter;
}
