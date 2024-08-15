using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Tenant Aggregate List Query Interface.
///     Query includes which tenants to query.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    ITenantAggregateListQuery<TAggregatePayload, in TQueryParameter, TQueryResponse> : IAggregateListQuery<
    TAggregatePayload
    , TQueryParameter, TQueryResponse> where TAggregatePayload : IAggregatePayloadCommon
    where TQueryParameter : ITenantListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse;
