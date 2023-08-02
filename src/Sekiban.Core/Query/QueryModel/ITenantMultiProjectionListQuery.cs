using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantMultiProjectionListQuery<TProjectionPayload, in TQueryParameter, TQueryResponse> : IMultiProjectionListQuery<TProjectionPayload,TQueryParameter, TQueryResponse>
    where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    where TQueryParameter : ITenantListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse;
