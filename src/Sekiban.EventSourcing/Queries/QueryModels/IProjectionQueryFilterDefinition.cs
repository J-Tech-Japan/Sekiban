using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface IProjectionQueryFilterDefinition<in TProjection, in TQueryParam, TResponseQueryModel>
    where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto where TQueryParam : IQueryParameter
{
    public TResponseQueryModel HandleFilter(TQueryParam queryParam, TProjection projection);
    public TResponseQueryModel HandleSortAndPagingIfNeeded(TQueryParam queryParam, TResponseQueryModel response);
}
