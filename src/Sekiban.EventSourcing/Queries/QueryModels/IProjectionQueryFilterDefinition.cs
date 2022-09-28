using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface IProjectionQueryFilterDefinition<in TProjection, TProjectionContents, in TQueryParameter, TResponseQueryModel>
    where TProjection : MultipleAggregateProjectionBase<TProjectionContents>
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel HandleFilter(TQueryParameter queryParam, MultipleAggregateProjectionContentsDto<TProjectionContents> projection);
    public TResponseQueryModel HandleSortAndPagingIfNeeded(TQueryParameter queryParam, TResponseQueryModel response);
}
