using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Interface for Aggregate Query.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    IAggregateQuery<TAggregatePayload, in TQueryParameter, out TQueryResponse> : IQueryHandlerCommon<TQueryParameter,
    TQueryResponse> where TAggregatePayload : IAggregatePayloadCommon
    where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
    where TQueryResponse : IQueryResponse
{
    /// <summary>
    ///     Make Query Result and returns it.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="list"></param>
    /// <returns></returns>
    public TQueryResponse HandleFilter(TQueryParameter queryParam, IEnumerable<AggregateState<TAggregatePayload>> list);
}
