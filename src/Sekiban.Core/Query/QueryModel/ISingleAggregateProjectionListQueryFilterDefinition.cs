using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, in TSingleAggregateProjection, TAggregateProjectionPayload, in TQueryParam,
        TResponseQueryModel> where TAggregate : IAggregatePayload, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>, new()
    where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
    where TQueryParam : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(
        TQueryParam queryParam,
        IEnumerable<SingleAggregateProjectionState<TAggregateProjectionPayload>> list);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParam queryParam, IEnumerable<TResponseQueryModel> projections);
}
