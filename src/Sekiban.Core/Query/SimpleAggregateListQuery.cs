using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query;

public record QueryAggregateState<TAggregatePayload>(AggregateState<TAggregatePayload> AggregateState) : IQueryOutput
    where TAggregatePayload : IAggregatePayload, new();
public record SimpleAggregateListQueryParameter<TAggregatePayload>(int? PageSize, int? PageNumber) : IQueryPagingParameter,
    IQueryInput<QueryAggregateState<TAggregatePayload>> where TAggregatePayload : IAggregatePayload, new();
public class SimpleAggregateListQuery<TAggregatePayload> : IAggregateListQuery<TAggregatePayload,
    SimpleAggregateListQueryParameter<TAggregatePayload>,
    QueryAggregateState<TAggregatePayload>> where TAggregatePayload : IAggregatePayload, new()
{
    public IEnumerable<QueryAggregateState<TAggregatePayload>> HandleFilter(
        SimpleAggregateListQueryParameter<TAggregatePayload> queryParam,
        IEnumerable<AggregateState<TAggregatePayload>> list) => list.Select(m => new QueryAggregateState<TAggregatePayload>(m));
    public IEnumerable<QueryAggregateState<TAggregatePayload>> HandleSort(
        SimpleAggregateListQueryParameter<TAggregatePayload> queryParam,
        IEnumerable<QueryAggregateState<TAggregatePayload>> filteredList) =>
        filteredList.OrderByDescending(m => m.AggregateState.LastSortableUniqueId);
}
