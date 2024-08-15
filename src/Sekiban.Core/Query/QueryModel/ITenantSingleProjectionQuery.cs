using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Single Projection Query interface for Tenant Query.
///     Query developer can implement this interface for Tenant Query.
/// </summary>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    ITenantSingleProjectionQuery<TSingleProjectionPayload, in TQueryParameter, out TQueryResponse> :
    ISingleProjectionQuery<
        TSingleProjectionPayload, TQueryParameter, TQueryResponse>
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQueryParameter : ITenantQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse;
