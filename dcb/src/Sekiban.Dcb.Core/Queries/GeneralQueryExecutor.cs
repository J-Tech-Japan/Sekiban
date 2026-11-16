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
            var method = queryType.GetMethod("HandleQuery", BindingFlags.Public | BindingFlags.Static);
            if (method is null)
            {
                return Unsupported<TMultiProjector, TQuery, IQueryContext, TOutput>(
                    $"Query type {queryType.FullName} must define a public static HandleQuery method.");
            }

            if (!typeof(TOutput).IsAssignableFrom(method.ReturnType))
            {
                return Unsupported<TMultiProjector, TQuery, IQueryContext, TOutput>(
                    UnsupportedShapeMessage(queryType, "HandleQuery", typeof(TOutput), method.ReturnType, isList: false));
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
                    out var unsupported)
                ?? Unsupported<TMultiProjector, TQuery, IQueryContext, IEnumerable<TOutput>>(unsupported!);
        }

        private static Func<IEnumerable<TOutput>, TQuery, IQueryContext, IEnumerable<TOutput>> CreateSort()
        {
            if (CoreHandlers is { } core)
            {
                return (items, query, context) => (IEnumerable<TOutput>)core.Sort(items, query, context);
            }

            return CreateByName<Func<IEnumerable<TOutput>, TQuery, IQueryContext, IEnumerable<TOutput>>>(
                    "HandleSort",
                    out var unsupported)
                ?? Unsupported<IEnumerable<TOutput>, TQuery, IQueryContext, IEnumerable<TOutput>>(unsupported!);
        }

        private static TDelegate? CreateByName<TDelegate>(string methodName, out string? unsupported)
            where TDelegate : Delegate
        {
            var queryType = typeof(TQuery);
            var method = queryType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method is null)
            {
                unsupported = $"List query type {queryType.FullName} must define a public static {methodName} method.";
                return null;
            }

            if (!typeof(IEnumerable<TOutput>).IsAssignableFrom(method.ReturnType))
            {
                unsupported = UnsupportedShapeMessage(
                    queryType,
                    methodName,
                    typeof(IEnumerable<TOutput>),
                    method.ReturnType,
                    isList: true);
                return null;
            }

            unsupported = null;
            return method.CreateDelegate<TDelegate>();
        }
    }

    // The invokers are cached per closed generic type, so an unsupported query shape must not fail the type
    // initializer: that would surface as a TypeInitializationException on every later use. Instead the cached delegate
    // throws the explanation each time the query is executed.
    private static Func<T1, T2, T3, TResult> Unsupported<T1, T2, T3, TResult>(string message) =>
        (_, _, _) => throw new InvalidOperationException(message);

    private static string UnsupportedShapeMessage(
        Type queryType,
        string methodName,
        Type expected,
        Type actual,
        bool isList)
    {
        var withResultContract = isList ? "IMultiProjectionListQuery" : "IMultiProjectionQuery";
        var coreContract = isList ? "ICoreMultiProjectionListQuery" : "ICoreMultiProjectionQuery";
        return $"Query type {queryType.FullName} cannot be executed: its {methodName} returns {FormatType(actual)}, " +
            $"but {FormatType(expected)} is required. A query whose handlers return a ResultBox must implement " +
            $"the WithResult {withResultContract} interface; otherwise implement {coreContract} and return the value directly.";
    }

    private static string FormatType(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>"
            : type.Name;
}
