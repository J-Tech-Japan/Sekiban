namespace Sekiban.Core.Query.QueryModel;

// ReSharper disable once UnusedTypeParameter
public interface IQueryHandlerCommon<in TQueryParameter, out TQueryResponse> where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse;
