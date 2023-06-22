using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantAggregateQuery <TAggregatePayload, in TQueryParameter, out TQueryResponse> : IAggregateQuery<TAggregatePayload, TQueryParameter,  TQueryResponse>
    where TAggregatePayload : IAggregatePayloadCommon where TQueryParameter : ITenantQueryParameter<TQueryResponse> where TQueryResponse : IQueryResponse
{
    
}
