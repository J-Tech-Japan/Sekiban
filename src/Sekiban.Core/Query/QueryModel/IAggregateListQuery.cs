using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IAggregateListQuery<TAggregatePayload, in TQueryParameter, TQueryResponse>
    where TAggregatePayload : IAggregatePayload, new()
    where TQueryParameter : IQueryParameter
{
    public IEnumerable<TQueryResponse> HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateState<TAggregatePayload>> list);
    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> filteredList);
}
