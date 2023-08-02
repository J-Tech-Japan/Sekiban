using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantSingleProjectionQuery<TSingleProjectionPayload, in TQueryParameter, out TQueryResponse> : ISingleProjectionQuery<TSingleProjectionPayload,
        TQueryParameter, TQueryResponse> where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQueryParameter : ITenantQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse;
