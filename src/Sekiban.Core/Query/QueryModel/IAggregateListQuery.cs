using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface
    IAggregateListQuery<TAggregatePayload, in TQueryParameter, TQueryResponse> : IListQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TAggregatePayload : IAggregatePayloadCommon
    where TQueryParameter : IListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    public IEnumerable<TQueryResponse> HandleFilter(
        TQueryParameter queryParam,
        IEnumerable<AggregateState<TAggregatePayload>> list);

    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> filteredList);
}
