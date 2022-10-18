using Sekiban.Core.Query.MultipleAggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface IProjectionListQueryFilterDefinition<in TProjection, TProjectionContents, in TQueryParameter, TResponseQueryModel>
    where TProjection : MultipleAggregateProjectionBase<TProjectionContents> where TProjectionContents : IMultipleAggregateProjectionContents, new()
{
    public IEnumerable<TResponseQueryModel> HandleFilter(
        TQueryParameter queryParam,
        MultipleAggregateProjectionContentsDto<TProjectionContents> projection);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParameter queryParam, IEnumerable<TResponseQueryModel> projections);
}
