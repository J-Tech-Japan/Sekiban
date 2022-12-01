using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;

namespace Sekiban.Core.Query.QueryModel;

public interface
    ISingleProjectionQuery<TSingleProjectionPayload, in TQueryParam, out TQueryResponse>
    where TSingleProjectionPayload : ISingleProjectionPayload
    where TQueryParam : IQueryParameter
{
    public TQueryResponse HandleFilter(
        TQueryParam queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> list);
}
