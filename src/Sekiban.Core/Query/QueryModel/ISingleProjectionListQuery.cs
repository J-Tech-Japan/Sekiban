using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Single Projection list query interface.
/// </summary>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
/// <typeparam name="TQueryParam"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    ISingleProjectionListQuery<TSingleProjectionPayload, in TQueryParam, TQueryResponse> : IListQueryHandlerCommon<
    TQueryParam, TQueryResponse> where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQueryParam : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParam>
    where TQueryResponse : IQueryResponse
{
    /// <summary>
    ///     Handle filter.
    ///     Query developers can implement this method to filter list.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="list">Input source</param>
    /// <returns></returns>
    public IEnumerable<TQueryResponse> HandleFilter(
        TQueryParam queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> list);
    /// <summary>
    ///     Handle sorting.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="filteredList"></param>
    /// <returns></returns>
    public IEnumerable<TQueryResponse> HandleSort(TQueryParam queryParam, IEnumerable<TQueryResponse> filteredList);
}
