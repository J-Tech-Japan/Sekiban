using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    IMultiProjectionListQuery<TProjectionPayload, in TQueryParameter, TQueryResponse>
    where TProjectionPayload : IMultiProjectionPayload, new()
{
    public IEnumerable<TQueryResponse> HandleFilter(
        TQueryParameter queryParam,
        MultiProjectionState<TProjectionPayload> projection);
    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> filteredList);
}
