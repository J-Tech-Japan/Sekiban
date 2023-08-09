using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Query;
/// <summary>
/// Generic aggregate list query parameter.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public class SimpleAggregateListQuery<TAggregatePayload> : IAggregateListQuery<TAggregatePayload, SimpleAggregateListQueryParameter<TAggregatePayload>
    , QueryAggregateState<TAggregatePayload>> where TAggregatePayload : IAggregatePayloadCommon
{
    public IEnumerable<QueryAggregateState<TAggregatePayload>> HandleFilter(
        SimpleAggregateListQueryParameter<TAggregatePayload> queryParam,
        IEnumerable<AggregateState<TAggregatePayload>> list) =>
        list.Select(m => new QueryAggregateState<TAggregatePayload>(m));
    public IEnumerable<QueryAggregateState<TAggregatePayload>> HandleSort(
        SimpleAggregateListQueryParameter<TAggregatePayload> queryParam,
        IEnumerable<QueryAggregateState<TAggregatePayload>> filteredList) =>
        filteredList.OrderByDescending(m => m.AggregateState.LastSortableUniqueId);
}
