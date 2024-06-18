using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextQueryGeneral<TOutput> : INextQueryGeneral where TOutput : notnull;
/// <summary>
///     Query Executor Interface.
///     Query user can use this interface to execute Query.
/// </summary>
public interface IQueryExecutor
{
    public Task<ResultBox<TOutput>> ExecuteNextAsync<TOutput>(INextQueryCommon<TOutput> query) where TOutput : notnull;
    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteNextAsync<TOutput>(INextListQueryCommon<TOutput> query) where TOutput : notnull;

    /// <summary>
    ///     Execute Query (List Query).
    ///     It could be Aggregate Query or Multi Projection Query or General Query
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="TOutput"></typeparam>
    /// <returns></returns>
    public Task<ListQueryResult<TOutput>> ExecuteAsync<TOutput>(IListQueryInput<TOutput> param) where TOutput : IQueryResponse;

    /// <summary>
    ///     Execute Query (List Query) with ResultBox.
    ///     It could be Aggregate Query or Multi Projection Query or General Query
    ///     If exception happened, it will catch and return ResultBox with exception.
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="TOutput"></typeparam>
    /// <returns></returns>
    public async Task<ResultBox<ListQueryResult<TOutput>>> ExecuteWithResultAsync<TOutput>(IListQueryInput<TOutput> param)
        where TOutput : IQueryResponse =>
        await ResultBox.WrapTry(async () => await ExecuteAsync(param));

    /// <summary>
    ///     Execute Query.
    ///     It could be Aggregate Query or Multi Projection Query or General Query
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="TOutput"></typeparam>
    /// <returns></returns>
    public Task<TOutput> ExecuteAsync<TOutput>(IQueryInput<TOutput> param) where TOutput : IQueryResponse;

    /// <summary>
    ///     Execute Query with ResultBox.
    ///     It could be Aggregate Query or Multi Projection Query or General Query
    ///     If exception happened, it will catch and return ResultBox with exception.
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="TOutput"></typeparam>
    /// <returns></returns>
    public async Task<ResultBox<TOutput>> ExecuteWithResultAsync<TOutput>(IQueryInput<TOutput> param) where TOutput : IQueryResponse =>
        await ResultBox.WrapTry(async () => await ExecuteAsync(param));
}
