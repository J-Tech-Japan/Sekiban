using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IAggregateListQueryFilterDefinition<TAggregate, TAggregateContents, in TQueryParameter, TResponseQueryModel>
    where TAggregate : AggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
    where TQueryParameter : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateDto<TAggregateContents>> list);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParameter queryParam, IEnumerable<TResponseQueryModel> projections);
}
