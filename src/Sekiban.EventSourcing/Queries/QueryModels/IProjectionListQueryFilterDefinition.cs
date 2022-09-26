using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface IProjectionListQueryFilterDefinition<in TProjection, in TQueryParameter, TResponseQueryModel>
    where TProjection : IMultipleAggregateProjectionDto where TQueryParameter : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(TQueryParameter queryParam, TProjection projection);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParameter queryParam, IEnumerable<TResponseQueryModel> projections);
}
