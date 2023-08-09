namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Interface for List Query Parameter.
///     implementing this interface supports Paging.
/// </summary>
/// <typeparam name="TQueryOutput"></typeparam>
public interface IListQueryPagingParameter<TQueryOutput> : IListQueryParameter<TQueryOutput>, IQueryPagingParameterCommon
    where TQueryOutput : IQueryResponse;
