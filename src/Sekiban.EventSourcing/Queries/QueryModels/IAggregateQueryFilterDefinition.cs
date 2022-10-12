using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface IAggregateQueryFilterDefinition<TAggregate, TAggregateContents, in TQueryParameter, TResponseQueryModel>
    where TAggregate : TransferableAggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateDto<TAggregateContents>> list);
}
