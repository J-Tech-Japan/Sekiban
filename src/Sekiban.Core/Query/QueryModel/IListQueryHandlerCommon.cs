namespace Sekiban.Core.Query.QueryModel;

// ReSharper disable once UnusedTypeParameter
public interface IListQueryHandlerCommon<in TQueryParameter, out TQueryResponse> where TQueryParameter : IListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
}
