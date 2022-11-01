using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ISingleProjectionQuery<TAggregatePayload, in TSingleProjection, TSingleProjectionPayload, in TQueryParam, out TQueryResponse>
    where TAggregatePayload : IAggregatePayload, new()
    where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
    where TSingleProjectionPayload : ISingleProjectionPayload
    where TQueryParam : IQueryParameter
{
    public TQueryResponse HandleFilter(
        TQueryParam queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> list);
}
