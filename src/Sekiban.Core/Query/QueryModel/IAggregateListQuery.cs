using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IAggregateListQuery<TAggregatePayload, in TQueryParameter, TResponseQueryModel>
    where TAggregatePayload : IAggregatePayload, new()
    where TQueryParameter : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateState<TAggregatePayload>> list);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParameter queryParam, IEnumerable<TResponseQueryModel> projections);
}
