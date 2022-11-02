using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query;

public class SimpleAggregateListQuery<TAggregatePayload> : IAggregateListQuery<TAggregatePayload,
    SimpleAggregateListQuery<TAggregatePayload>.QueryParameter,
    AggregateIdentifierState<TAggregatePayload>> where TAggregatePayload : IAggregatePayload, new()
{
    public IEnumerable<AggregateIdentifierState<TAggregatePayload>> HandleFilter(
        QueryParameter queryParam,
        IEnumerable<AggregateIdentifierState<TAggregatePayload>> list) => list;
    public IEnumerable<AggregateIdentifierState<TAggregatePayload>> HandleSort(
        QueryParameter queryParam,
        IEnumerable<AggregateIdentifierState<TAggregatePayload>> projections)
    {
        return projections.OrderByDescending(m => m.LastSortableUniqueId);
    }
    public record QueryParameter(int? PageSize, int? PageNumber) : IQueryPagingParameter;
}
