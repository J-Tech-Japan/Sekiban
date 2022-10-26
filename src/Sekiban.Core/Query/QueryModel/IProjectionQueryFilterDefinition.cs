using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IProjectionQueryFilterDefinition<in TProjection, TProjectionPayload, in TQueryParameter, TResponseQueryModel>
    where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>
    where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel HandleFilter(TQueryParameter queryParam, MultipleAggregateProjectionState<TProjectionPayload> projection);
}
