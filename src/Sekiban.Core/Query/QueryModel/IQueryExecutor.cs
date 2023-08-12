namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Executor Interface.
///     Query user can use this interface to execute Query.
/// </summary>
public interface IQueryExecutor
{
    /// <summary>
    ///     Execute Query (List Query).
    ///     It could be Aggregate Query or Multi Projection Query or General Query
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="TOutput"></typeparam>
    /// <returns></returns>
    public Task<ListQueryResult<TOutput>> ExecuteAsync<TOutput>(IListQueryInput<TOutput> param) where TOutput : IQueryResponse;
    /// <summary>
    ///     Execute Query.
    ///     It could be Aggregate Query or Multi Projection Query or General Query
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="TOutput"></typeparam>
    /// <returns></returns>
    public Task<TOutput> ExecuteAsync<TOutput>(IQueryInput<TOutput> param) where TOutput : IQueryResponse;
}
