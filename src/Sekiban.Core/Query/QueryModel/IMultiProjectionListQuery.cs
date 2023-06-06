using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface
    IMultiProjectionListQuery<TProjectionPayload, in TQueryParameter, TQueryResponse> : IListQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    where TQueryParameter : IListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    public IEnumerable<TQueryResponse> HandleFilter(TQueryParameter queryParam, MultiProjectionState<TProjectionPayload> projection);

    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> filteredList);
}
