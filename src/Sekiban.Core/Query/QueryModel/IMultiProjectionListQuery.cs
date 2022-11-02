using Sekiban.Core.Query.MultipleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    IMultiProjectionListQuery<TProjection, TProjectionPayload, in TQueryParameter, TQueryResponse> : IMultiProjectionQueryBase<TProjection>
    where TProjection : MultiProjectionBase<TProjectionPayload> where TProjectionPayload : IMultiProjectionPayload, new()
{
    public IEnumerable<TQueryResponse> HandleFilter(
        TQueryParameter queryParam,
        MultiProjectionState<TProjectionPayload> projection);
    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> projections);
}
