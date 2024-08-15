namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     List Query Interface for General Query.
///     General Query means no specific source but it can be used to get multiple projection by using
///     IMultiProjectionService as DI.
/// </summary>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    IGeneralListQuery<in TQueryParameter, TQueryResponse> : IListQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TQueryParameter : IListQueryParameter<TQueryResponse> where TQueryResponse : IQueryResponse
{
    /// <summary>
    ///     returns filtered list.
    ///     data can be retrieved from IMultiProjectionService.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <returns></returns>
    public Task<IEnumerable<TQueryResponse>> HandleFilterAsync(TQueryParameter queryParam);

    /// <summary>
    ///     Sort Filtered List and returns sorted list.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="filteredList"></param>
    /// <returns></returns>
    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> filteredList);
}
