namespace Sekiban.Core.Query.QueryModel;

public interface IGeneralListQuery<in TQueryParameter, TQueryResponse> : IListQueryHandlerCommon<TQueryParameter, TQueryResponse>
    where TQueryParameter : IListQueryParameter<TQueryResponse> where TQueryResponse : IQueryResponse
{
    public Task<IEnumerable<TQueryResponse>> HandleFilterAsync(TQueryParameter queryParam);

    public IEnumerable<TQueryResponse> HandleSort(TQueryParameter queryParam, IEnumerable<TQueryResponse> filteredList);
}
