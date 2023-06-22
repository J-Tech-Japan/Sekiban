using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface IMultiProjectionQuery<TProjectionPayload, in TQueryParameter, TQueryResponse> : IQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    public TQueryResponse HandleFilter(TQueryParameter queryParam, MultiProjectionState<TProjectionPayload> projection);
}
