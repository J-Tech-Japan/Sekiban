using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ISingleProjectionListQuery<TAggregate, in TSingleProjection, TAggregateProjectionPayload, in TQueryParam,
        TResponseQueryModel> where TAggregate : IAggregatePayload, new()
    where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TAggregateProjectionPayload>, new()
    where TAggregateProjectionPayload : ISingleProjectionPayload
    where TQueryParam : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(
        TQueryParam queryParam,
        IEnumerable<SingleProjectionState<TAggregateProjectionPayload>> list);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParam queryParam, IEnumerable<TResponseQueryModel> projections);
}
