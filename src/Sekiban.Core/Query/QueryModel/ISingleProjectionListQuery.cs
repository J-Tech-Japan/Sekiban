using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ISingleProjectionListQuery<TAggregatePayload, in TSingleProjection, TSingleProjectionPayload, in TQueryParam,
        TQueryResponse> where TAggregatePayload : IAggregatePayload, new()
    where TSingleProjection : ProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
    where TSingleProjectionPayload : ISingleProjectionPayload
    where TQueryParam : IQueryParameter
{
    public IEnumerable<TQueryResponse> HandleFilter(
        TQueryParam queryParam,
        IEnumerable<ProjectionState<TSingleProjectionPayload>> list);
    public IEnumerable<TQueryResponse> HandleSort(TQueryParam queryParam, IEnumerable<TQueryResponse> projections);
}
