using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IProjectionQueryFilterDefinition<in TProjection, TProjectionContents, in TQueryParameter, TResponseQueryModel>
    where TProjection : MultipleAggregateProjectionBase<TProjectionContents>
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel HandleFilter(TQueryParameter queryParam, MultipleAggregateProjectionContentsDto<TProjectionContents> projection);
}
