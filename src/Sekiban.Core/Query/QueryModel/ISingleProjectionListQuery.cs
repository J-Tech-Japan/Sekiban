using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ISingleProjectionListQuery<TSingleProjectionPayload, in TQueryParam,
        TQueryResponse>
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQueryParam : IQueryParameterCommon
{
    public IEnumerable<TQueryResponse> HandleFilter(
        TQueryParam queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> list);

    public IEnumerable<TQueryResponse> HandleSort(TQueryParam queryParam, IEnumerable<TQueryResponse> filteredList);
}
