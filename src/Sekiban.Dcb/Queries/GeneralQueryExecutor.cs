using ResultBoxes;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Queries;

/// <summary>
/// General executor for multi-projection queries
/// </summary>
public class GeneralQueryExecutor
{
    private readonly IServiceProvider _serviceProvider;
    
    public GeneralQueryExecutor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }
    
    /// <summary>
    /// Execute a single result query
    /// </summary>
    /// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
    /// <typeparam name="TQuery">The query type</typeparam>
    /// <typeparam name="TOutput">The output type</typeparam>
    /// <param name="query">The query to execute</param>
    /// <param name="projectorProvider">Function to provide the multi-projector state</param>
    /// <returns>The query result</returns>
    public async Task<ResultBox<TOutput>> ExecuteQueryAsync<TMultiProjector, TQuery, TOutput>(
        TQuery query,
        Func<Task<ResultBox<TMultiProjector>>> projectorProvider)
        where TMultiProjector : IMultiProjector<TMultiProjector>
        where TQuery : IMultiProjectionQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        var projectorResult = await projectorProvider();
        if (!projectorResult.IsSuccess)
        {
            return ResultBox.Error<TOutput>(projectorResult.GetException());
        }
        
        var projector = projectorResult.GetValue();
        var context = new QueryContext(_serviceProvider);
        
        return TQuery.HandleQuery(projector, query, context);
    }
    
    /// <summary>
    /// Execute a list query with filtering, sorting, and pagination
    /// </summary>
    /// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
    /// <typeparam name="TQuery">The query type</typeparam>
    /// <typeparam name="TOutput">The output type of each item</typeparam>
    /// <param name="query">The query to execute</param>
    /// <param name="projectorProvider">Function to provide the multi-projector state</param>
    /// <returns>The paginated query result</returns>
    public async Task<ResultBox<ListQueryResult<TOutput>>> ExecuteListQueryAsync<TMultiProjector, TQuery, TOutput>(
        TQuery query,
        Func<Task<ResultBox<TMultiProjector>>> projectorProvider)
        where TMultiProjector : IMultiProjector<TMultiProjector>
        where TQuery : IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        var projectorResult = await projectorProvider();
        if (!projectorResult.IsSuccess)
        {
            return ResultBox.Error<ListQueryResult<TOutput>>(projectorResult.GetException());
        }
        
        var projector = projectorResult.GetValue();
        var context = new QueryContext(_serviceProvider);
        
        // Filter
        var filterResult = TQuery.HandleFilter(projector, query, context);
        if (!filterResult.IsSuccess)
        {
            return ResultBox.Error<ListQueryResult<TOutput>>(filterResult.GetException());
        }
        
        var filteredItems = filterResult.GetValue();
        
        // Sort
        var sortResult = TQuery.HandleSort(filteredItems, query, context);
        if (!sortResult.IsSuccess)
        {
            return ResultBox.Error<ListQueryResult<TOutput>>(sortResult.GetException());
        }
        
        var sortedItems = sortResult.GetValue().ToList();
        
        // Apply pagination
        var result = ListQueryResult<TOutput>.CreatePaginated(query, sortedItems);
        
        return ResultBox.FromValue(result);
    }
    
    /// <summary>
    /// Execute a query with a custom handler function
    /// </summary>
    /// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
    /// <typeparam name="TOutput">The output type</typeparam>
    /// <param name="projectorProvider">Function to provide the multi-projector state</param>
    /// <param name="handler">Custom handler function</param>
    /// <returns>The query result</returns>
    public async Task<ResultBox<TOutput>> ExecuteWithHandlerAsync<TMultiProjector, TOutput>(
        Func<Task<ResultBox<TMultiProjector>>> projectorProvider,
        Func<TMultiProjector, IQueryContext, ResultBox<TOutput>> handler)
        where TMultiProjector : IMultiProjector<TMultiProjector>
        where TOutput : notnull
    {
        var projectorResult = await projectorProvider();
        if (!projectorResult.IsSuccess)
        {
            return ResultBox.Error<TOutput>(projectorResult.GetException());
        }
        
        var projector = projectorResult.GetValue();
        var context = new QueryContext(_serviceProvider);
        
        return handler(projector, context);
    }
    
    /// <summary>
    /// Execute a list query with custom filter and sort functions
    /// </summary>
    /// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
    /// <typeparam name="TOutput">The output type of each item</typeparam>
    /// <param name="projectorProvider">Function to provide the multi-projector state</param>
    /// <param name="filter">Custom filter function</param>
    /// <param name="sort">Custom sort function</param>
    /// <param name="pagingParameter">Optional paging parameters</param>
    /// <returns>The paginated query result</returns>
    public async Task<ResultBox<ListQueryResult<TOutput>>> ExecuteListWithHandlersAsync<TMultiProjector, TOutput>(
        Func<Task<ResultBox<TMultiProjector>>> projectorProvider,
        Func<TMultiProjector, IQueryContext, ResultBox<IEnumerable<TOutput>>> filter,
        Func<IEnumerable<TOutput>, IQueryContext, ResultBox<IEnumerable<TOutput>>> sort,
        IQueryPagingParameter? pagingParameter = null)
        where TMultiProjector : IMultiProjector<TMultiProjector>
        where TOutput : notnull
    {
        var projectorResult = await projectorProvider();
        if (!projectorResult.IsSuccess)
        {
            return ResultBox.Error<ListQueryResult<TOutput>>(projectorResult.GetException());
        }
        
        var projector = projectorResult.GetValue();
        var context = new QueryContext(_serviceProvider);
        
        // Filter
        var filterResult = filter(projector, context);
        if (!filterResult.IsSuccess)
        {
            return ResultBox.Error<ListQueryResult<TOutput>>(filterResult.GetException());
        }
        
        var filteredItems = filterResult.GetValue();
        
        // Sort
        var sortResult = sort(filteredItems, context);
        if (!sortResult.IsSuccess)
        {
            return ResultBox.Error<ListQueryResult<TOutput>>(sortResult.GetException());
        }
        
        var sortedItems = sortResult.GetValue().ToList();
        
        // Apply pagination
        var result = pagingParameter != null
            ? ListQueryResult<TOutput>.CreatePaginated(pagingParameter, sortedItems)
            : new ListQueryResult<TOutput>(
                sortedItems.Count,
                null,
                null,
                null,
                sortedItems);
        
        return ResultBox.FromValue(result);
    }
}