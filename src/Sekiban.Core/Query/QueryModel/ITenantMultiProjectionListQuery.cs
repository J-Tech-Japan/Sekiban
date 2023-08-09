using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Multi Projection Query Interface for Tenant Query.
///     Query developer can implement this interface for Tenant Query.
/// </summary>
/// <typeparam name="TProjectionPayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    ITenantMultiProjectionListQuery<TProjectionPayload, in TQueryParameter, TQueryResponse> : IMultiProjectionListQuery<TProjectionPayload,
        TQueryParameter, TQueryResponse> where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    where TQueryParameter : ITenantListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse;
