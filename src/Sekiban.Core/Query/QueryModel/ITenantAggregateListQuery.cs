using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantAggregateListQuery<TAggregatePayload, in TQueryParameter, TQueryResponse> : IAggregateListQuery<TAggregatePayload, TQueryParameter,
        TQueryResponse> where TAggregatePayload : IAggregatePayloadCommon
    where TQueryParameter : ITenantListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse;
