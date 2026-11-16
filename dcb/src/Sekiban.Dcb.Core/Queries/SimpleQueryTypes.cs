using Sekiban.Dcb.MultiProjections;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Simple implementation of ICoreQueryTypes using explicit registration
/// </summary>
public class SimpleQueryTypes : ICoreQueryTypes
{
    private readonly ConcurrentDictionary<Type, Type> _queryToOutputMap = new();
    private readonly ConcurrentDictionary<Type, Type> _queryToProjectorMap = new();
    private readonly ConcurrentBag<Type> _queryTypes = new();
    private readonly ConcurrentBag<Type> _responseTypes = new();
    private readonly ConcurrentDictionary<string, Type> _typeNameMap = new();

    public IEnumerable<Type> GetQueryTypes() => _queryTypes;

    public IEnumerable<Type> GetQueryResponseTypes() => _responseTypes.Distinct();

    private readonly ConcurrentDictionary<Type, CoreQueryContractInvoker.SingleHandler?> _coreSingleHandlers = new();
    private readonly ConcurrentDictionary<Type, (CoreQueryContractInvoker.FilterHandler Filter, CoreQueryContractInvoker.SortHandler Sort)?>
        _coreListHandlers = new();

    public async Task<object> ExecuteQueryAsync(
        IQueryCommon query,
        Func<Task<IMultiProjectionPayload>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        var queryType = query.GetType();

        if (!_queryToProjectorMap.TryGetValue(queryType, out _))
        {
            throw new InvalidOperationException($"Query type {queryType.Name} is not registered");
        }

        if (!_queryToOutputMap.TryGetValue(queryType, out _))
        {
            throw new InvalidOperationException($"Output type for query {queryType.Name} is not registered");
        }

        // A query that implements the core contract is handled through it; a query that only declares a public static
        // HandleQuery returning its output (the exception-based facade) is invoked by name.
        var coreHandler = _coreSingleHandlers.GetOrAdd(queryType, CoreQueryContractInvoker.TryCreateSingle);
        var handleMethod = coreHandler is null
            ? queryType.GetMethod("HandleQuery", BindingFlags.Public | BindingFlags.Static)
            : null;

        if (coreHandler is null && handleMethod == null)
        {
            throw new InvalidOperationException($"Query type {queryType.Name} does not have HandleQuery method");
        }

        // Get the projector
        var projector = await projectorProvider();
        var context = new QueryContext(serviceProvider, safeVersion, safeWindowThreshold, safeWindowThresholdTime, unsafeVersion);

        var result = coreHandler is not null
            ? coreHandler(projector, query, context)
            : handleMethod!.Invoke(null, BindingFlags.DoNotWrapExceptions, null, new object[] { projector, query, context }, null);

        return result ?? throw new InvalidOperationException("HandleQuery returned null result");
    }

    public async Task<object> ExecuteListQueryAsync(
        IListQueryCommon query,
        Func<Task<IMultiProjectionPayload>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        var queryType = query.GetType();

        if (!_queryToProjectorMap.TryGetValue(queryType, out _))
        {
            throw new InvalidOperationException($"List query type {queryType.Name} is not registered");
        }

        if (!_queryToOutputMap.TryGetValue(queryType, out var outputType))
        {
            throw new InvalidOperationException($"Output type for list query {queryType.Name} is not registered");
        }

        // Get the projector
        var projector = await projectorProvider();
        var context = new QueryContext(serviceProvider, safeVersion, safeWindowThreshold, safeWindowThresholdTime, unsafeVersion);

        var coreHandlers = _coreListHandlers.GetOrAdd(queryType, CoreQueryContractInvoker.TryCreateList);
        MethodInfo? handleFilterMethod = null;
        MethodInfo? handleSortMethod = null;
        if (coreHandlers is null)
        {
            // Get HandleFilter method
            handleFilterMethod = queryType.GetMethod("HandleFilter", BindingFlags.Public | BindingFlags.Static);

            if (handleFilterMethod == null)
            {
                throw new InvalidOperationException($"List query type {queryType.Name} does not have HandleFilter method");
            }

            // Get HandleSort method
            handleSortMethod = queryType.GetMethod("HandleSort", BindingFlags.Public | BindingFlags.Static);

            if (handleSortMethod == null)
            {
                throw new InvalidOperationException($"List query type {queryType.Name} does not have HandleSort method");
            }
        }

        // Execute filter
        var filterResult = coreHandlers is { } filterHandlers
            ? filterHandlers.Filter(projector, query, context)
            : handleFilterMethod!.Invoke(null, BindingFlags.DoNotWrapExceptions, null, new object[] { projector, query, context }, null);
        var filteredItems = ExtractEnumerableResult(filterResult);

        // Execute sort
        var sortResult = coreHandlers is { } sortHandlers
            ? sortHandlers.Sort(filteredItems, query, context)
            : handleSortMethod!.Invoke(null, BindingFlags.DoNotWrapExceptions, null, new object?[] { filteredItems, query, context }, null);
        var sortedItems = ExtractEnumerableResult(sortResult).Cast<object>();

        // Apply pagination if query implements IQueryPagingParameter
        if (query is IQueryPagingParameter pagingParam)
        {
            var listType = typeof(List<>).MakeGenericType(outputType);
            var list = Activator.CreateInstance(listType, sortedItems);

            var createPaginatedMethod = typeof(ListQueryResult<>)
                .MakeGenericType(outputType)
                .GetMethod("CreatePaginated", BindingFlags.Public | BindingFlags.Static);

            var result = createPaginatedMethod?.Invoke(null, new[] { pagingParam, list });
            return result!;
        }
        else
        {
            var items = sortedItems?.ToList() ?? new List<object>();
            var resultType = typeof(ListQueryResult<>).MakeGenericType(outputType);
            var result = Activator.CreateInstance(resultType, items.Count, null, null, null, items);
            return result!;
        }
    }

    public async Task<ListQueryResultGeneral> ExecuteListQueryAsGeneralAsync(
        IListQueryCommon query,
        Func<Task<IMultiProjectionPayload>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        // First execute the query normally
        var value = await ExecuteListQueryAsync(
            query,
            projectorProvider,
            serviceProvider,
            safeVersion,
            safeWindowThreshold,
            safeWindowThresholdTime,
            unsafeVersion);

        // Use reflection to extract the properties from ListQueryResult<T>
        var valueType = value.GetType();
        if (!valueType.IsGenericType || valueType.GetGenericTypeDefinition() != typeof(ListQueryResult<>))
        {
            throw new InvalidOperationException($"Expected ListQueryResult<T> but got {valueType.Name}");
        }

        // Extract properties using reflection
        var totalCount = valueType.GetProperty("TotalCount")?.GetValue(value) as int?;
        var totalPages = valueType.GetProperty("TotalPages")?.GetValue(value) as int?;
        var currentPage = valueType.GetProperty("CurrentPage")?.GetValue(value) as int?;
        var pageSize = valueType.GetProperty("PageSize")?.GetValue(value) as int?;
        var items = valueType.GetProperty("Items")?.GetValue(value) as IEnumerable;

        // Convert items to IEnumerable<object>
        var objectItems = new List<object>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item != null)
                {
                    objectItems.Add(item);
                }
            }
        }

        // Get the item type for RecordType
        var itemType = valueType.GetGenericArguments()[0];
        var recordType = itemType.FullName ?? itemType.Name;

        return new ListQueryResultGeneral(totalCount, totalPages, currentPage, pageSize, objectItems, recordType, query);
    }

    public Type GetMultiProjectorType(IQueryCommon query)
    {
        var queryType = query.GetType();

        if (_queryToProjectorMap.TryGetValue(queryType, out var projectorType))
        {
            return projectorType;
        }

        throw new InvalidOperationException($"Query type {queryType.Name} is not registered");
    }

    public Type GetMultiProjectorType(IListQueryCommon query)
    {
        var queryType = query.GetType();

        if (_queryToProjectorMap.TryGetValue(queryType, out var projectorType))
        {
            return projectorType;
        }

        throw new InvalidOperationException($"List query type {queryType.Name} is not registered");
    }

    public Type? GetTypeByName(string typeName) => _typeNameMap.TryGetValue(typeName, out var type) ? type : null;

    /// <summary>
    ///     Register a multi-projection query type
    /// </summary>
    /// <typeparam name="TProjector">The multi-projector type</typeparam>
    /// <typeparam name="TQuery">The query type</typeparam>
    /// <typeparam name="TOutput">The output type</typeparam>
    public void RegisterQuery<TProjector, TQuery, TOutput>() where TProjector : ICoreMultiProjector<TProjector>
        where TQuery : IQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        var queryType = typeof(TQuery);
        var projectorType = typeof(TProjector);
        var outputType = typeof(TOutput);

        var (inferredProjector, inferredOutput) = ExtractQuerySignature(queryType);
        if (inferredProjector != projectorType || inferredOutput != outputType)
        {
            throw new InvalidOperationException(
                $"Query type '{queryType.Name}' generic parameters do not match registration. " +
                $"Interface declares {inferredProjector.Name} -> {inferredOutput.Name}, " +
                $"registration supplied {projectorType.Name} -> {outputType.Name}");
        }

        // Check if already registered
        if (!_queryToProjectorMap.TryAdd(queryType, projectorType))
        {
            // Check if it's the same registration
            if (_queryToProjectorMap.TryGetValue(queryType, out var existingProjector) &&
                _queryToOutputMap.TryGetValue(queryType, out var existingOutput))
            {
                if (existingProjector != projectorType || existingOutput != outputType)
                {
                    throw new InvalidOperationException(
                        $"Query type '{queryType.Name}' is already registered with different types. " +
                        $"Existing: {existingProjector.Name} -> {existingOutput.Name}, " +
                        $"New: {projectorType.Name} -> {outputType.Name}");
                }
            }
            return; // Already registered with same types
        }

        _queryToOutputMap[queryType] = outputType;
        _queryTypes.Add(queryType);
        _responseTypes.Add(outputType);
        _typeNameMap[queryType.Name] = queryType;
        _typeNameMap[queryType.FullName ?? queryType.Name] = queryType;
    }

    /// <summary>
    ///     Register a multi-projection query type by extracting generic parameters from the query type
    /// </summary>
    /// <typeparam name="TQuery">The query type that implements ICoreMultiProjectionQuery</typeparam>
    public void RegisterQuery<TQuery>() where TQuery : IQueryCommon
    {
        var queryType = typeof(TQuery);

        var (projectorType, outputType) = ExtractQuerySignature(queryType);

        // Check if already registered
        if (!_queryToProjectorMap.TryAdd(queryType, projectorType))
        {
            // Check if it's the same registration
            if (_queryToProjectorMap.TryGetValue(queryType, out var existingProjector) &&
                _queryToOutputMap.TryGetValue(queryType, out var existingOutput))
            {
                if (existingProjector != projectorType || existingOutput != outputType)
                {
                    throw new InvalidOperationException(
                        $"Query type '{queryType.Name}' is already registered with different types. " +
                        $"Existing: {existingProjector.Name} -> {existingOutput.Name}, " +
                        $"New: {projectorType.Name} -> {outputType.Name}");
                }
            }
            return; // Already registered with same types
        }

        _queryToOutputMap[queryType] = outputType;
        _queryTypes.Add(queryType);
        _responseTypes.Add(outputType);
        _typeNameMap[queryType.Name] = queryType;
        _typeNameMap[queryType.FullName ?? queryType.Name] = queryType;
    }

    /// <summary>
    ///     Register a multi-projection list query type
    /// </summary>
    /// <typeparam name="TProjector">The multi-projector type</typeparam>
    /// <typeparam name="TQuery">The query type</typeparam>
    /// <typeparam name="TOutput">The output type</typeparam>
    public void RegisterListQuery<TProjector, TQuery, TOutput>() where TProjector : ICoreMultiProjector<TProjector>
        where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        var queryType = typeof(TQuery);
        var projectorType = typeof(TProjector);
        var outputType = typeof(TOutput);

        var (inferredProjector, inferredOutput) = ExtractListQuerySignature(queryType);
        if (inferredProjector != projectorType || inferredOutput != outputType)
        {
            throw new InvalidOperationException(
                $"List query type '{queryType.Name}' generic parameters do not match registration. " +
                $"Interface declares {inferredProjector.Name} -> {inferredOutput.Name}, " +
                $"registration supplied {projectorType.Name} -> {outputType.Name}");
        }

        // Check if already registered
        if (!_queryToProjectorMap.TryAdd(queryType, projectorType))
        {
            // Check if it's the same registration
            if (_queryToProjectorMap.TryGetValue(queryType, out var existingProjector) &&
                _queryToOutputMap.TryGetValue(queryType, out var existingOutput))
            {
                if (existingProjector != projectorType || existingOutput != outputType)
                {
                    throw new InvalidOperationException(
                        $"List query type '{queryType.Name}' is already registered with different types. " +
                        $"Existing: {existingProjector.Name} -> {existingOutput.Name}, " +
                        $"New: {projectorType.Name} -> {outputType.Name}");
                }
            }
            return; // Already registered with same types
        }

        _queryToOutputMap[queryType] = outputType;
        _queryTypes.Add(queryType);
        _responseTypes.Add(outputType);
        _typeNameMap[queryType.Name] = queryType;
        _typeNameMap[queryType.FullName ?? queryType.Name] = queryType;
    }

    /// <summary>
    ///     Register a multi-projection list query type by extracting generic parameters from the query type
    /// </summary>
    /// <typeparam name="TQuery">The query type that implements ICoreMultiProjectionListQuery</typeparam>
    public void RegisterListQuery<TQuery>() where TQuery : IListQueryCommon
    {
        var queryType = typeof(TQuery);

        var (projectorType, outputType) = ExtractListQuerySignature(queryType);

        // Check if already registered
        if (!_queryToProjectorMap.TryAdd(queryType, projectorType))
        {
            // Check if it's the same registration
            if (_queryToProjectorMap.TryGetValue(queryType, out var existingProjector) &&
                _queryToOutputMap.TryGetValue(queryType, out var existingOutput))
            {
                if (existingProjector != projectorType || existingOutput != outputType)
                {
                    throw new InvalidOperationException(
                        $"List query type '{queryType.Name}' is already registered with different types. " +
                        $"Existing: {existingProjector.Name} -> {existingOutput.Name}, " +
                        $"New: {projectorType.Name} -> {outputType.Name}");
                }
            }
            return; // Already registered with same types
        }

        _queryToOutputMap[queryType] = outputType;
        _queryTypes.Add(queryType);
        _responseTypes.Add(outputType);
        _typeNameMap[queryType.Name] = queryType;
        _typeNameMap[queryType.FullName ?? queryType.Name] = queryType;
    }

    private static IEnumerable<object> ExtractEnumerableResult(object? result) =>
        result is IEnumerable enumerable
            ? enumerable.Cast<object>()
            : throw new InvalidOperationException("Query handler returned invalid result type");

    private static (Type ProjectorType, Type OutputType) ExtractQuerySignature(Type queryType)
    {
        // Check for ICoreMultiProjectionQuery<,,> (Core/WithResult packages)
        var queryInterface = queryType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(ICoreMultiProjectionQuery<,,>));

        // If not found, check for IMultiProjectionQuery<,,> by name (WithoutResult package)
        // This allows both core-contract and exception-based facade queries to be registered
        if (queryInterface == null)
        {
            queryInterface = queryType
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                    i.GetGenericArguments().Length == 3 &&
                    i.Name == "IMultiProjectionQuery`3");
        }

        if (queryInterface == null)
        {
            throw new InvalidOperationException(
                $"Query type '{queryType.Name}' must implement either ICoreMultiProjectionQuery<,,> or IMultiProjectionQuery<,,>.");
        }

        var genericArgs = queryInterface.GetGenericArguments();
        return (genericArgs[0], genericArgs[2]);
    }

    private static (Type ProjectorType, Type OutputType) ExtractListQuerySignature(Type queryType)
    {
        // Check for ICoreMultiProjectionListQuery<,,> (Core/WithResult packages)
        var queryInterface = queryType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(ICoreMultiProjectionListQuery<,,>));

        // If not found, check for IMultiProjectionListQuery<,,> by name (WithoutResult package)
        // This allows both core-contract and exception-based facade queries to be registered
        if (queryInterface == null)
        {
            queryInterface = queryType
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                    i.GetGenericArguments().Length == 3 &&
                    i.Name == "IMultiProjectionListQuery`3");
        }

        if (queryInterface == null)
        {
            throw new InvalidOperationException(
                $"Query type '{queryType.Name}' must implement either ICoreMultiProjectionListQuery<,,> or IMultiProjectionListQuery<,,>.");
        }

        var genericArgs = queryInterface.GetGenericArguments();
        return (genericArgs[0], genericArgs[2]);
    }
}
