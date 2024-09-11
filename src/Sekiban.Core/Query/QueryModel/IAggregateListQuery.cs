using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     List Query Interface for Aggregate List.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    IAggregateListQuery<TAggregatePayload, in TQueryParameter, TQueryResponse> : IListQueryHandlerCommon<TQueryParameter
    ,
    TQueryResponse> where TAggregatePayload : IAggregatePayloadCommon
    where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
    where TQueryResponse : IQueryResponse
{
    /// <summary>
    ///     Filter Aggregate List and returns filtered list.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="list"></param>
    /// <returns></returns>
    public IEnumerable<TQueryResponse> HandleFilter(
        TQueryParameter queryParam,
        IEnumerable<AggregateState<TAggregatePayload>> list);

    /// <summary>
    ///     Sort Filtered List and returns sorted list.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="filteredList"></param>
    /// <returns></returns>
    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> filteredList);
}
