using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IAggregateQuery<TAggregatePayload, in TQueryParameter, out TQueryResponse>
    where TAggregatePayload : IAggregatePayload, new()
    where TQueryParameter : IQueryParameter
{
    public TQueryResponse HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateIdentifierState<TAggregatePayload>> list);
}