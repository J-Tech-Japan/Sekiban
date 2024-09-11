using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Multi Projection Query interface for Tenant Query.
/// </summary>
/// <typeparam name="TProjectionPayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    ITenantMultiProjectionQuery<TProjectionPayload, in TQueryParameter, TQueryResponse> : IMultiProjectionQuery<
    TProjectionPayload, TQueryParameter, TQueryResponse> where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    where TQueryParameter : ITenantQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
    where TQueryResponse : IQueryResponse;
