using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface
    IMultiProjectionQuery<TProjection, TProjectionPayload, in TQueryParameter, TQueryResponse> : IMultiProjectionQueryBase<TProjection>
    where TProjection : MultiProjectionBase<TProjectionPayload>
    where TProjectionPayload : IMultiProjectionPayload, new()
    where TQueryParameter : IQueryParameter
{
    public TQueryResponse HandleFilter(TQueryParameter queryParam, MultiProjectionState<TProjectionPayload> projection);
}
