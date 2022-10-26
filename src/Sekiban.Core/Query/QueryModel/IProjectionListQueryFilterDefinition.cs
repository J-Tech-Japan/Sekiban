using Sekiban.Core.Query.MultipleAggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface IProjectionListQueryFilterDefinition<in TProjection, TProjectionPayload, in TQueryParameter, TResponseQueryModel>
    where TProjection : MultipleAggregateProjectionBase<TProjectionPayload> where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
{
    public IEnumerable<TResponseQueryModel> HandleFilter(
        TQueryParameter queryParam,
        MultipleAggregateProjectionState<TProjectionPayload> projection);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParameter queryParam, IEnumerable<TResponseQueryModel> projections);
}
