namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Handler Interface.
///     Query Developers does not need to implement this interface directly.
/// </summary>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
// ReSharper disable once UnusedTypeParameter
public interface IQueryHandlerCommon<in TQueryParameter, out TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse> where TQueryResponse : IQueryResponse;
