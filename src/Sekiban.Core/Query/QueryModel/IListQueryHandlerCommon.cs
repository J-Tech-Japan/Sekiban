namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Common Interface for List Query Handler.
///     Aggregate Developers does not need to implement this interface directly.
/// </summary>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
// ReSharper disable once UnusedTypeParameter
public interface IListQueryHandlerCommon<in TQueryParameter, out TQueryResponse>
    where TQueryParameter : IListQueryParameter<TQueryResponse> where TQueryResponse : IQueryResponse;
