using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IAggregateListQueryFilterDefinition< TAggregateContents, in TQueryParameter, TResponseQueryModel>
    where TAggregateContents : IAggregatePayload, new()
    where TQueryParameter : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateState<TAggregateContents>> list);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParameter queryParam, IEnumerable<TResponseQueryModel> projections);
}
