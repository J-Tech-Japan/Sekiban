using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Aggregate Query with Tenant interface.
///     Query developers implement this interface for the query with tenant.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    ITenantAggregateQuery<TAggregatePayload, in TQueryParameter, out TQueryResponse> : IAggregateQuery<TAggregatePayload, TQueryParameter,
        TQueryResponse>
    where TAggregatePayload : IAggregatePayloadCommon
    where TQueryParameter : ITenantQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse;
