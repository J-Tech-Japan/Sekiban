using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface IAggregateDtoListQueryFilterDefinition<TAggregateContents, in TQueryParam, TResponseQueryModel>
    where TAggregateContents : IAggregateContents, new() where TQueryParam : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(TQueryParam queryParam, IEnumerable<AggregateDto<TAggregateContents>> list);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParam queryParam, IEnumerable<TResponseQueryModel> projections);
}
