using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Interface for Multi Projection Query.
/// </summary>
/// <typeparam name="TProjectionPayload"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface
    IMultiProjectionQuery<TProjectionPayload, in TQueryParameter, TQueryResponse> : IQueryHandlerCommon<TQueryParameter,
    TQueryResponse> where TProjectionPayload : IMultiProjectionPayloadCommon
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    /// <summary>
    ///     receive multi projection and returns result.
    /// </summary>
    /// <param name="queryParam"></param>
    /// <param name="projection"></param>
    /// <returns></returns>
    public TQueryResponse HandleFilter(TQueryParameter queryParam, MultiProjectionState<TProjectionPayload> projection);
}
