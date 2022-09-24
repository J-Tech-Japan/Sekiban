using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface IProjectionListQueryFilterDefinition<in TProjection, in TQueryParam, TResponseQueryModel>
    where TProjection : IMultipleAggregateProjectionDto where TQueryParam : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(TQueryParam queryParam, TProjection projection);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParam queryParam, IEnumerable<TResponseQueryModel> projections);
}
