namespace Sekiban.Core.Query.QueryModel;

public interface IGeneralQuery<in TQueryParameter, TQueryResponse> : IQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse> where TQueryResponse : IQueryResponse
{
    public Task<TQueryResponse> HandleFilterAsync(TQueryParameter queryParam);
}
