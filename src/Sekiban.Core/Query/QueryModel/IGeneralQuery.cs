namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Interface for General Query.
///     General Query means no specific source but it can be used to get multiple projection by using
///     IMultiProjectionService as DI.
/// </summary>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
public interface IGeneralQuery<in TQueryParameter, TQueryResponse> : IQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse> where TQueryResponse : IQueryResponse
{
    public Task<TQueryResponse> HandleFilterAsync(TQueryParameter queryParam);
}
