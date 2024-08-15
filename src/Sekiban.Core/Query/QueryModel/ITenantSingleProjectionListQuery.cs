using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Single Projection Query interface for Tenant Query.
///     Query developer can implement this interface for Tenant Query.
/// </summary>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
/// <typeparam name="TQueryParam"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    ITenantSingleProjectionListQuery<TSingleProjectionPayload, in TQueryParam, TQueryResponse> :
    ISingleProjectionListQuery<
        TSingleProjectionPayload, TQueryParam, TQueryResponse>
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQueryParam : ITenantListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse;
