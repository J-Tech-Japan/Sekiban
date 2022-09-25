using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface IAggregateListQueryFilterDefinition<TAggregate, TAggregateContents, in TQueryParameter, TResponseQueryModel>
    where TAggregate : TransferableAggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
    where TQueryParameter : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateDto<TAggregateContents>> list);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParameter queryParam, IEnumerable<TResponseQueryModel> projections);
}
