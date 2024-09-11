using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Single Projection Query Interface.
/// </summary>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    ISingleProjectionQuery<TSingleProjectionPayload, in TQueryParameter, out TQueryResponse> : IQueryHandlerCommon<
    TQueryParameter, TQueryResponse> where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
    where TQueryResponse : IQueryResponse
{
    /// <summary>
    ///     Handle Single Projection Query.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="list"></param>
    /// <returns></returns>
    public TQueryResponse HandleFilter(
        TQueryParameter queryParam,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> list);
}
