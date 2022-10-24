using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IAggregateQueryFilterDefinition<TAggregateContents, in TQueryParameter, TResponseQueryModel>
    where TAggregateContents : IAggregatePayload, new()
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateState<TAggregateContents>> list);
}
