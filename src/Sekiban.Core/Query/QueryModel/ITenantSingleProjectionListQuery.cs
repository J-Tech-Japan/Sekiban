using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantSingleProjectionListQuery<TSingleProjectionPayload, in TQueryParam, TQueryResponse> : ISingleProjectionListQuery<TSingleProjectionPayload,
        TQueryParam, TQueryResponse> where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQueryParam : ITenantListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
}
