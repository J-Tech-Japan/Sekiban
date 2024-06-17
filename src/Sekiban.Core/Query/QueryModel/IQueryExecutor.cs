using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryContext;
public interface INextQueryGeneral : IQueryPartitionKeyCommon
{
}
public interface INextQueryAsyncGeneral;
public interface INextQueryCommon : INextQueryGeneral
{
}
public interface INextQueryCommon<TOutput> : INextQueryCommon where TOutput : notnull;
public interface INextListQueryCommon : INextQueryGeneral
{
}
public interface INextListQueryCommon<TOutput> : INextListQueryCommon where TOutput : notnull;
public interface INextAggregateQueryCommon<TAggregatePayload, TOutput> : INextQueryCommon<TOutput>
    where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;
public interface INextAggregateQuery<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>
    where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<TOutput> HandleFilter(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
}
public interface INextAggregateQueryAsync<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>, INextQueryAsyncGeneral
    where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public Task<ResultBox<TOutput>> HandleFilterAsync(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
}
public interface INextAggregateListQuery<TAggregatePayload, TOutput> : INextListQueryCommon<TOutput> where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
{
    public ResultBox<IEnumerable<TOutput>> HandleFilter(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}
public interface INextAggregateListQueryAsync<TAggregatePayload, TOutput> : INextListQueryCommon<TOutput> where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
{
    public Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
    public Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(IEnumerable<TOutput> filteredList, IQueryContext context);
}
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
