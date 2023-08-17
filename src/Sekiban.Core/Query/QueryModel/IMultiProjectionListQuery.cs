using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Interface for Multi Projection List Query.
/// </summary>
/// <typeparam name="TProjectionPayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    IMultiProjectionListQuery<TProjectionPayload, in TQueryParameter, TQueryResponse> : IListQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TProjectionPayload : IMultiProjectionPayloadCommon
    where TQueryParameter : IListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    /// <summary>
    ///     returns filtered list.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="projection"></param>
    /// <returns></returns>
    public IEnumerable<TQueryResponse> HandleFilter(TQueryParameter queryParam, MultiProjectionState<TProjectionPayload> projection);
    /// <summary>
    ///     sort filtered list and returns sorted list.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="filteredList"></param>
    /// <returns></returns>
    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> filteredList);
}
