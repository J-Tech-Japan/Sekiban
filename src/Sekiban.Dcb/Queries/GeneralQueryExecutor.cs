using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     General executor for multi-projection queries
/// </summary>
public class GeneralQueryExecutor
{
    private readonly IServiceProvider _serviceProvider;

    public GeneralQueryExecutor(IServiceProvider serviceProvider) => _serviceProvider
        = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    /// <summary>
    ///     Execute a single result query
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
        where TQuery : IQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        var projectorResult = await projectorProvider();
        if (!projectorResult.IsSuccess)
        {
            return ResultBox.Error<TOutput>(projectorResult.GetException());
        }

        var projector = projectorResult.GetValue();
        var context = new QueryContext(_serviceProvider);

        return MultiProjectionQueryInvoker<TMultiProjector, TQuery, TOutput>.Handle(projector, query, context);
    }

    /// <summary>
    ///     Execute a list query with filtering, sorting, and pagination
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
        where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
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
        var filterResult = MultiProjectionListQueryInvoker<TMultiProjector, TQuery, TOutput>
            .Filter(projector, query, context);
        if (!filterResult.IsSuccess)
        {
            return ResultBox.Error<ListQueryResult<TOutput>>(filterResult.GetException());
        }

        var filteredItems = filterResult.GetValue();

        // Sort
        var sortResult = MultiProjectionListQueryInvoker<TMultiProjector, TQuery, TOutput>
            .Sort(filteredItems, query, context);
        if (!sortResult.IsSuccess)
        {
            return ResultBox.Error<ListQueryResult<TOutput>>(sortResult.GetException());
        }

        var sortedItems = sortResult.GetValue().ToList();

        // Apply pagination
        var result = query is IQueryPagingParameter pagingParameter
            ? ListQueryResult<TOutput>.CreatePaginated(pagingParameter, sortedItems)
            : new ListQueryResult<TOutput>(sortedItems.Count, null, null, null, sortedItems);

        return ResultBox.FromValue(result);
    }

    /// <summary>
    ///     Execute a query with a custom handler function
    /// </summary>
    /// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
    /// <typeparam name="TOutput">The output type</typeparam>
    /// <param name="projectorProvider">Function to provide the multi-projector state</param>
    /// <param name="handler">Custom handler function</param>
    /// <returns>The query result</returns>
    public async Task<ResultBox<TOutput>> ExecuteWithHandlerAsync<TMultiProjector, TOutput>(
        Func<Task<ResultBox<TMultiProjector>>> projectorProvider,
        Func<TMultiProjector, IQueryContext, ResultBox<TOutput>> handler)
        where TMultiProjector : IMultiProjector<TMultiProjector> where TOutput : notnull
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
    ///     Execute a list query with custom filter and sort functions
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
        IQueryPagingParameter? pagingParameter = null) where TMultiProjector : IMultiProjector<TMultiProjector>
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
            : new ListQueryResult<TOutput>(sortedItems.Count, null, null, null, sortedItems);

        return ResultBox.FromValue(result);
    }

    private static class MultiProjectionQueryInvoker<TMultiProjector, TQuery, TOutput>
        where TMultiProjector : IMultiProjector<TMultiProjector>
        where TQuery : IQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        public static readonly Func<TMultiProjector, TQuery, IQueryContext, ResultBox<TOutput>> Handle =
            CreateHandler();

        private static Func<TMultiProjector, TQuery, IQueryContext, ResultBox<TOutput>> CreateHandler()
        {
            var queryType = typeof(TQuery);
            var method = queryType.GetMethod("HandleQuery", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"Query type {queryType.Name} must define a public static HandleQuery method.");

            if (method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(ResultBox<>))
            {
                var returnArg = method.ReturnType.GetGenericArguments()[0];
                if (returnArg != typeof(TOutput))
                {
                    throw new InvalidOperationException(
                        $"HandleQuery return type mismatch. Expected ResultBox<{typeof(TOutput).Name}> but got ResultBox<{returnArg.Name}>.");
                }

                var del = method.CreateDelegate<Func<TMultiProjector, TQuery, IQueryContext, ResultBox<TOutput>>>();
                return del;
            }

            if (!typeof(TOutput).IsAssignableFrom(method.ReturnType))
            {
                throw new InvalidOperationException(
                    $"HandleQuery return type mismatch. Expected {typeof(TOutput).Name} or ResultBox<{typeof(TOutput).Name}> but got {method.ReturnType.Name}.");
            }

            var valueDelegate =
                method.CreateDelegate<Func<TMultiProjector, TQuery, IQueryContext, TOutput>>();

            return (projector, query, context) =>
            {
                try
                {
                    var value = valueDelegate(projector, query, context);
                    return ResultBox.FromValue(value);
                }
                catch (Exception ex)
                {
                    return ResultBox.Error<TOutput>(ex);
                }
            };
        }
    }

    private static class MultiProjectionListQueryInvoker<TMultiProjector, TQuery, TOutput>
        where TMultiProjector : IMultiProjector<TMultiProjector>
        where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        public static readonly Func<TMultiProjector, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>> Filter =
            CreateFilter();

        public static readonly Func<IEnumerable<TOutput>, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>>
            Sort = CreateSort();

        private static Func<TMultiProjector, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>> CreateFilter()
        {
            var queryType = typeof(TQuery);
            var method = queryType.GetMethod("HandleFilter", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"List query type {queryType.Name} must define a public static HandleFilter method.");

            if (method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(ResultBox<>))
            {
                var returnArg = method.ReturnType.GetGenericArguments()[0];
                if (!typeof(IEnumerable<TOutput>).IsAssignableFrom(returnArg))
                {
                    throw new InvalidOperationException(
                        $"HandleFilter return type mismatch. Expected ResultBox<IEnumerable<{typeof(TOutput).Name}>> but got ResultBox<{returnArg.Name}>.");
                }

                var del = method.CreateDelegate<Func<TMultiProjector, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>>>();
                return del;
            }

            if (!typeof(IEnumerable<TOutput>).IsAssignableFrom(method.ReturnType))
            {
                throw new InvalidOperationException(
                    $"HandleFilter return type mismatch. Expected IEnumerable<{typeof(TOutput).Name}> or ResultBox<IEnumerable<{typeof(TOutput).Name}>> but got {method.ReturnType.Name}.");
            }

            var valueDelegate =
                method.CreateDelegate<Func<TMultiProjector, TQuery, IQueryContext, IEnumerable<TOutput>>>();

            return (projector, query, context) =>
            {
                try
                {
                    var value = valueDelegate(projector, query, context);
                    return ResultBox.FromValue(value);
                }
                catch (Exception ex)
                {
                    return ResultBox.Error<IEnumerable<TOutput>>(ex);
                }
            };
        }

        private static Func<IEnumerable<TOutput>, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>> CreateSort()
        {
            var queryType = typeof(TQuery);
            var method = queryType.GetMethod("HandleSort", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"List query type {queryType.Name} must define a public static HandleSort method.");

            if (method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(ResultBox<>))
            {
                var returnArg = method.ReturnType.GetGenericArguments()[0];
                if (!typeof(IEnumerable<TOutput>).IsAssignableFrom(returnArg))
                {
                    throw new InvalidOperationException(
                        $"HandleSort return type mismatch. Expected ResultBox<IEnumerable<{typeof(TOutput).Name}>> but got ResultBox<{returnArg.Name}>.");
                }

                var del = method.CreateDelegate<Func<IEnumerable<TOutput>, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>>>();
                return del;
            }

            if (!typeof(IEnumerable<TOutput>).IsAssignableFrom(method.ReturnType))
            {
                throw new InvalidOperationException(
                    $"HandleSort return type mismatch. Expected IEnumerable<{typeof(TOutput).Name}> or ResultBox<IEnumerable<{typeof(TOutput).Name}>> but got {method.ReturnType.Name}.");
            }

            var valueDelegate =
                method.CreateDelegate<Func<IEnumerable<TOutput>, TQuery, IQueryContext, IEnumerable<TOutput>>>();

            return (items, query, context) =>
            {
                try
                {
                    var value = valueDelegate(items, query, context);
                    return ResultBox.FromValue(value);
                }
                catch (Exception ex)
                {
                    return ResultBox.Error<IEnumerable<TOutput>>(ex);
                }
            };
        }
    }
}
