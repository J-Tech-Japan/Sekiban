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
    public async Task<TOutput> ExecuteQueryAsync<TMultiProjector, TQuery, TOutput>(
        TQuery query,
        Func<Task<TMultiProjector>> projectorProvider)
        where TMultiProjector : ICoreMultiProjector<TMultiProjector>
        where TQuery : IQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        var projector = await projectorProvider();
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
    public async Task<ListQueryResult<TOutput>> ExecuteListQueryAsync<TMultiProjector, TQuery, TOutput>(
        TQuery query,
        Func<Task<TMultiProjector>> projectorProvider)
        where TMultiProjector : ICoreMultiProjector<TMultiProjector>
        where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        var projector = await projectorProvider();
        var context = new QueryContext(_serviceProvider);

        // Filter
        var filteredItems = MultiProjectionListQueryInvoker<TMultiProjector, TQuery, TOutput>
            .Filter(projector, query, context);

        // Sort
        var sortedItems = MultiProjectionListQueryInvoker<TMultiProjector, TQuery, TOutput>
            .Sort(filteredItems, query, context)
            .ToList();

        // Apply pagination
        return query is IQueryPagingParameter pagingParameter
            ? ListQueryResult<TOutput>.CreatePaginated(pagingParameter, sortedItems)
            : new ListQueryResult<TOutput>(sortedItems.Count, null, null, null, sortedItems);
    }

    /// <summary>
    ///     Execute a query with a custom handler function
    /// </summary>
    /// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
    /// <typeparam name="TOutput">The output type</typeparam>
    /// <param name="projectorProvider">Function to provide the multi-projector state</param>
    /// <param name="handler">Custom handler function</param>
    /// <returns>The query result</returns>
    public async Task<TOutput> ExecuteWithHandlerAsync<TMultiProjector, TOutput>(
        Func<Task<TMultiProjector>> projectorProvider,
        Func<TMultiProjector, IQueryContext, TOutput> handler)
        where TMultiProjector : ICoreMultiProjector<TMultiProjector> where TOutput : notnull
    {
        var projector = await projectorProvider();
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
    public async Task<ListQueryResult<TOutput>> ExecuteListWithHandlersAsync<TMultiProjector, TOutput>(
        Func<Task<TMultiProjector>> projectorProvider,
        Func<TMultiProjector, IQueryContext, IEnumerable<TOutput>> filter,
        Func<IEnumerable<TOutput>, IQueryContext, IEnumerable<TOutput>> sort,
        IQueryPagingParameter? pagingParameter = null) where TMultiProjector : ICoreMultiProjector<TMultiProjector>
        where TOutput : notnull
    {
        var projector = await projectorProvider();
        var context = new QueryContext(_serviceProvider);

        // Filter
        var filteredItems = filter(projector, context);

        // Sort
        var sortedItems = sort(filteredItems, context).ToList();

        // Apply pagination
        return pagingParameter != null
            ? ListQueryResult<TOutput>.CreatePaginated(pagingParameter, sortedItems)
            : new ListQueryResult<TOutput>(sortedItems.Count, null, null, null, sortedItems);
    }

    private static class MultiProjectionQueryInvoker<TMultiProjector, TQuery, TOutput>
        where TMultiProjector : ICoreMultiProjector<TMultiProjector>
        where TQuery : IQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        public static readonly Func<TMultiProjector, TQuery, IQueryContext, TOutput> Handle = CreateHandler();

        private static Func<TMultiProjector, TQuery, IQueryContext, TOutput> CreateHandler()
        {
            // A query implementing the core contract (directly, or through a facade interface that unwraps its own
            // result type once) is handled through that contract.
            if (CoreQueryContractInvoker.TryCreateSingle(typeof(TQuery)) is { } coreHandler)
            {
                return (projector, query, context) => (TOutput)coreHandler(projector, query, context)!;
            }

            var queryType = typeof(TQuery);
            var method = queryType.GetMethod("HandleQuery", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"Query type {queryType.Name} must define a public static HandleQuery method.");

            if (!typeof(TOutput).IsAssignableFrom(method.ReturnType))
            {
                throw new InvalidOperationException(
                    $"HandleQuery return type mismatch. Expected {typeof(TOutput).Name} but got {method.ReturnType.Name}.");
            }

            return method.CreateDelegate<Func<TMultiProjector, TQuery, IQueryContext, TOutput>>();
        }
    }

    private static class MultiProjectionListQueryInvoker<TMultiProjector, TQuery, TOutput>
        where TMultiProjector : ICoreMultiProjector<TMultiProjector>
        where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        private static readonly (CoreQueryContractInvoker.FilterHandler Filter, CoreQueryContractInvoker.SortHandler Sort)?
            CoreHandlers = CoreQueryContractInvoker.TryCreateList(typeof(TQuery));

        public static readonly Func<TMultiProjector, TQuery, IQueryContext, IEnumerable<TOutput>> Filter =
            CreateFilter();

        public static readonly Func<IEnumerable<TOutput>, TQuery, IQueryContext, IEnumerable<TOutput>>
            Sort = CreateSort();

        private static Func<TMultiProjector, TQuery, IQueryContext, IEnumerable<TOutput>> CreateFilter()
        {
            if (CoreHandlers is { } core)
            {
                return (projector, query, context) => (IEnumerable<TOutput>)core.Filter(projector, query, context);
            }

            return CreateByName<Func<TMultiProjector, TQuery, IQueryContext, IEnumerable<TOutput>>>(
                "HandleFilter",
                "List query type {0} must define a public static HandleFilter method.");
        }

        private static Func<IEnumerable<TOutput>, TQuery, IQueryContext, IEnumerable<TOutput>> CreateSort()
        {
            if (CoreHandlers is { } core)
            {
                return (items, query, context) => (IEnumerable<TOutput>)core.Sort(items, query, context);
            }

            return CreateByName<Func<IEnumerable<TOutput>, TQuery, IQueryContext, IEnumerable<TOutput>>>(
                "HandleSort",
                "List query type {0} must define a public static HandleSort method.");
        }

        private static TDelegate CreateByName<TDelegate>(string methodName, string missingMessage)
            where TDelegate : Delegate
        {
            var queryType = typeof(TQuery);
            var method = queryType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(string.Format(missingMessage, queryType.Name));

            if (!typeof(IEnumerable<TOutput>).IsAssignableFrom(method.ReturnType))
            {
                throw new InvalidOperationException(
                    $"{methodName} return type mismatch. Expected IEnumerable<{typeof(TOutput).Name}> but got {method.ReturnType.Name}.");
            }

            return method.CreateDelegate<TDelegate>();
        }
    }
}
