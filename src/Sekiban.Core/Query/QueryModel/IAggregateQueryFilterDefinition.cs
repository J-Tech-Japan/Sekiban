using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IAggregateQueryFilterDefinition<TAggregate, TAggregateContents, in TQueryParameter, TResponseQueryModel>
    where TAggregate : AggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateDto<TAggregateContents>> list);
}
