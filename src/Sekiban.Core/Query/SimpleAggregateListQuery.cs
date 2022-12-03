using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query;

public record QueryAggregateState<TAggregatePayload>(AggregateState<TAggregatePayload> AggregateState) : IQueryOutput
    where TAggregatePayload : IAggregatePayload, new();

public class SimpleAggregateListQuery<TAggregatePayload> : IAggregateListQuery<TAggregatePayload,
    SimpleAggregateListQuery<TAggregatePayload>.QueryParameter,
    AggregateState<TAggregatePayload>> where TAggregatePayload : IAggregatePayload, new()
{
    public IEnumerable<AggregateState<TAggregatePayload>> HandleFilter(
        QueryParameter<TAggregatePayload> queryParam,
        IEnumerable<AggregateState<TAggregatePayload>> list) => list;

    public IEnumerable<AggregateState<TAggregatePayload>> HandleSort(
        QueryParameter<TAggregatePayload> queryParam,
        IEnumerable<AggregateState<TAggregatePayload>> filteredList)
    {
        return filteredList.OrderByDescending(m => m.LastSortableUniqueId);
    }

    public record QueryParameter<TAggregatePayload>(int? PageSize, int? PageNumber) : IQueryPagingParameter,
        IQueryInput<QueryAggregateState<TAggregatePayload>> where TAggregatePayload : IAggregatePayload, new();
}
