using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ISingleProjectionQuery<TSingleProjectionPayload, in TQueryParameter, out TQueryResponse> : IQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    public TQueryResponse HandleFilter(
        TQueryParameter queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> list);
}
