using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IAggregateQuery<TAggregatePayload, in TQueryParameter, out TQueryResponse> : IQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TAggregatePayload : IAggregatePayloadCommon
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    public TQueryResponse HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateState<TAggregatePayload>> list);
}
