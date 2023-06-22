using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantMultiProjectionQuery<TProjectionPayload, in TQueryParameter, TQueryResponse> : IMultiProjectionQuery<TProjectionPayload, TQueryParameter,
        TQueryResponse> where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    where TQueryParameter : ITenantQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
}
