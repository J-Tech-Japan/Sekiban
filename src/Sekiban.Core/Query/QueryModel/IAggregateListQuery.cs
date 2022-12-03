using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface
    IAggregateListQuery<TAggregatePayload, in TQueryParameter, TQueryResponse> : IListQueryCommon<TQueryParameter, TQueryResponse>
    where TAggregatePayload : IAggregatePayload, new()
    where TQueryParameter : IQueryParameter, IQueryInput<TQueryResponse>
    where TQueryResponse : IQueryOutput
{
    public IEnumerable<TQueryResponse> HandleFilter(
        TQueryParameter queryParam,
        IEnumerable<AggregateState<TAggregatePayload>> list);

    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> filteredList);
}
