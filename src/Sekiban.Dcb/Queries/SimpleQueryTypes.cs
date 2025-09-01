using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Simple implementation of IQueryTypes using explicit registration
/// </summary>
public class SimpleQueryTypes : IQueryTypes
{
    private readonly ConcurrentDictionary<Type, Type> _queryToOutputMap = new();
    private readonly ConcurrentDictionary<Type, Type> _queryToProjectorMap = new();
    private readonly ConcurrentBag<Type> _queryTypes = new();
    private readonly ConcurrentBag<Type> _responseTypes = new();
    private readonly ConcurrentDictionary<string, Type> _typeNameMap = new();

    public IEnumerable<Type> GetQueryTypes() => _queryTypes;

    public IEnumerable<Type> GetQueryResponseTypes() => _responseTypes.Distinct();

    public async Task<ResultBox<object>> ExecuteQueryAsync(
        IQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null,
    int? unsafeVersion = null)
    {
        var queryType = query.GetType();

        if (!_queryToProjectorMap.TryGetValue(queryType, out var projectorType))
        {
            return ResultBox.Error<object>(
                new InvalidOperationException($"Query type {queryType.Name} is not registered"));
        }

        if (!_queryToOutputMap.TryGetValue(queryType, out var outputType))
        {
            return ResultBox.Error<object>(
                new InvalidOperationException($"Output type for query {queryType.Name} is not registered"));
        }

        // Get the HandleQuery method
        var handleMethod = queryType.GetMethod("HandleQuery", BindingFlags.Public | BindingFlags.Static);

        if (handleMethod == null)
        {
            return ResultBox.Error<object>(
                new InvalidOperationException($"Query type {queryType.Name} does not have HandleQuery method"));
        }

        // Get the projector
        var projectorResult = await projectorProvider();
        if (!projectorResult.IsSuccess)
        {
            return ResultBox.Error<object>(projectorResult.GetException());
        }

        var projector = projectorResult.GetValue();
    var context = new QueryContext(serviceProvider, safeVersion, safeWindowThreshold, safeWindowThresholdTime, unsafeVersion);

        // Invoke the static method
        try
        {
            var result = handleMethod.Invoke(null, new object[] { projector, query, context });

            // Check if result is a ResultBox<T>
            if (result != null &&
                result.GetType().IsGenericType &&
                result.GetType().GetGenericTypeDefinition().Name == "ResultBox`1")
            {
                var isSuccess = result.GetType().GetProperty("IsSuccess")?.GetValue(result) as bool?;

                if (isSuccess == true)
                {
                    var getValue = result.GetType().GetMethod("GetValue");
                    var value = getValue?.Invoke(result, null);
                    return ResultBox.FromValue(value!);
                }
                var getException = result.GetType().GetMethod("GetException");
                var exception = getException?.Invoke(result, null) as Exception;
                return ResultBox.Error<object>(exception ?? new Exception("Query execution failed"));
            }

            return ResultBox.Error<object>(new InvalidOperationException("Invalid result from HandleQuery"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<object>(ex);
        }
    }

    public async Task<ResultBox<object>> ExecuteListQueryAsync(
        IListQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null,
    int? unsafeVersion = null)
    {
        var queryType = query.GetType();

        if (!_queryToProjectorMap.TryGetValue(queryType, out var projectorType))
        {
            return ResultBox.Error<object>(
                new InvalidOperationException($"List query type {queryType.Name} is not registered"));
        }

        if (!_queryToOutputMap.TryGetValue(queryType, out var outputType))
        {
            return ResultBox.Error<object>(
                new InvalidOperationException($"Output type for list query {queryType.Name} is not registered"));
        }

        // Get the projector
        var projectorResult = await projectorProvider();
        if (!projectorResult.IsSuccess)
        {
            return ResultBox.Error<object>(projectorResult.GetException());
        }

        var projector = projectorResult.GetValue();
    var context = new QueryContext(serviceProvider, safeVersion, safeWindowThreshold, safeWindowThresholdTime, unsafeVersion);

        // Get HandleFilter method
        var handleFilterMethod = queryType.GetMethod("HandleFilter", BindingFlags.Public | BindingFlags.Static);

        if (handleFilterMethod == null)
        {
            return ResultBox.Error<object>(
                new InvalidOperationException($"List query type {queryType.Name} does not have HandleFilter method"));
        }

        // Get HandleSort method
        var handleSortMethod = queryType.GetMethod("HandleSort", BindingFlags.Public | BindingFlags.Static);

        if (handleSortMethod == null)
        {
            return ResultBox.Error<object>(
                new InvalidOperationException($"List query type {queryType.Name} does not have HandleSort method"));
        }

        try
        {
            // Execute filter
            var filterResult = handleFilterMethod.Invoke(null, new object[] { projector, query, context });
            if (filterResult == null ||
                !filterResult.GetType().IsGenericType ||
                filterResult.GetType().GetGenericTypeDefinition().Name != "ResultBox`1")
            {
                return ResultBox.Error<object>(new Exception("Filter execution failed - invalid result type"));
            }

            var filterIsSuccess = filterResult.GetType().GetProperty("IsSuccess")?.GetValue(filterResult) as bool?;
            if (filterIsSuccess != true)
            {
                var filterException
                    = filterResult.GetType().GetMethod("GetException")?.Invoke(filterResult, null) as Exception;
                return ResultBox.Error<object>(filterException ?? new Exception("Filter execution failed"));
            }

            var getFilterValue = filterResult.GetType().GetMethod("GetValue");
            var filteredItems = getFilterValue?.Invoke(filterResult, null);

            // Execute sort
            var sortResult = handleSortMethod.Invoke(null, new[] { filteredItems, query, context });
            if (sortResult == null ||
                !sortResult.GetType().IsGenericType ||
                sortResult.GetType().GetGenericTypeDefinition().Name != "ResultBox`1")
            {
                return ResultBox.Error<object>(new Exception("Sort execution failed - invalid result type"));
            }

            var sortIsSuccess = sortResult.GetType().GetProperty("IsSuccess")?.GetValue(sortResult) as bool?;
            if (sortIsSuccess != true)
            {
                var sortException
                    = sortResult.GetType().GetMethod("GetException")?.Invoke(sortResult, null) as Exception;
                return ResultBox.Error<object>(sortException ?? new Exception("Sort execution failed"));
            }

            var getSortValue = sortResult.GetType().GetMethod("GetValue");
            var sortedItems = getSortValue?.Invoke(sortResult, null) as IEnumerable<object>;

            // Apply pagination if query implements IQueryPagingParameter
            if (query is IQueryPagingParameter pagingParam)
            {
                var listType = typeof(List<>).MakeGenericType(outputType);
                var list = Activator.CreateInstance(listType, sortedItems);

                var createPaginatedMethod = typeof(ListQueryResult<>)
                    .MakeGenericType(outputType)
                    .GetMethod("CreatePaginated", BindingFlags.Public | BindingFlags.Static);

                var result = createPaginatedMethod?.Invoke(null, new[] { pagingParam, list });
                return ResultBox.FromValue<object>(result!);
            } else
            {
                var items = sortedItems?.ToList() ?? new List<object>();
                var resultType = typeof(ListQueryResult<>).MakeGenericType(outputType);
                var result = Activator.CreateInstance(resultType, items.Count, null, null, null, items);
                return ResultBox.FromValue(result!);
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<object>(ex);
        }
    }

    public async Task<ResultBox<ListQueryResultGeneral>> ExecuteListQueryAsGeneralAsync(
        IListQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null,
    int? unsafeVersion = null)
    {
        // First execute the query normally
        var result = await ExecuteListQueryAsync(
            query,
            projectorProvider,
            serviceProvider,
            safeVersion,
            safeWindowThreshold,
            safeWindowThresholdTime,
            unsafeVersion);

        if (!result.IsSuccess)
        {
            return ResultBox.Error<ListQueryResultGeneral>(result.GetException());
        }

        var value = result.GetValue();

        // Use reflection to extract the properties from ListQueryResult<T>
        var valueType = value.GetType();
        if (!valueType.IsGenericType || valueType.GetGenericTypeDefinition() != typeof(ListQueryResult<>))
        {
            return ResultBox.Error<ListQueryResultGeneral>(
                new InvalidOperationException($"Expected ListQueryResult<T> but got {valueType.Name}"));
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

        return ResultBox.FromValue(
            new ListQueryResultGeneral(totalCount, totalPages, currentPage, pageSize, objectItems, recordType, query));
    }

    public ResultBox<Type> GetMultiProjectorType(IQueryCommon query)
    {
        var queryType = query.GetType();

        if (_queryToProjectorMap.TryGetValue(queryType, out var projectorType))
        {
            return ResultBox.FromValue(projectorType);
        }

        return ResultBox.Error<Type>(new InvalidOperationException($"Query type {queryType.Name} is not registered"));
    }

    public ResultBox<Type> GetMultiProjectorType(IListQueryCommon query)
    {
        var queryType = query.GetType();

        if (_queryToProjectorMap.TryGetValue(queryType, out var projectorType))
        {
            return ResultBox.FromValue(projectorType);
        }

        return ResultBox.Error<Type>(
            new InvalidOperationException($"List query type {queryType.Name} is not registered"));
    }

    public Type? GetTypeByName(string typeName) => _typeNameMap.TryGetValue(typeName, out var type) ? type : null;

    /// <summary>
    ///     Register a multi-projection query type
    /// </summary>
    /// <typeparam name="TProjector">The multi-projector type</typeparam>
    /// <typeparam name="TQuery">The query type</typeparam>
    /// <typeparam name="TOutput">The output type</typeparam>
    public void RegisterQuery<TProjector, TQuery, TOutput>() where TProjector : IMultiProjector<TProjector>
        where TQuery : IMultiProjectionQuery<TProjector, TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        var queryType = typeof(TQuery);
        var projectorType = typeof(TProjector);
        var outputType = typeof(TOutput);

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
    /// <typeparam name="TQuery">The query type that implements IMultiProjectionQuery</typeparam>
    public void RegisterQuery<TQuery>() where TQuery : IQueryCommon
    {
        var queryType = typeof(TQuery);

        // Find the IMultiProjectionQuery interface
        var queryInterface = queryType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMultiProjectionQuery<,,>));

        if (queryInterface == null)
        {
            throw new InvalidOperationException(
                $"Query type '{queryType.Name}' does not implement IMultiProjectionQuery<,,>");
        }

        // Extract the generic arguments
        var genericArgs = queryInterface.GetGenericArguments();
        var projectorType = genericArgs[0];
        var outputType = genericArgs[2];

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
    public void RegisterListQuery<TProjector, TQuery, TOutput>() where TProjector : IMultiProjector<TProjector>
        where TQuery : IMultiProjectionListQuery<TProjector, TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        var queryType = typeof(TQuery);
        var projectorType = typeof(TProjector);
        var outputType = typeof(TOutput);

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
    /// <typeparam name="TQuery">The query type that implements IMultiProjectionListQuery</typeparam>
    public void RegisterListQuery<TQuery>() where TQuery : IListQueryCommon
    {
        var queryType = typeof(TQuery);

        // Find the IMultiProjectionListQuery interface
        var listQueryInterface = queryType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IMultiProjectionListQuery<,,>));

        if (listQueryInterface == null)
        {
            throw new InvalidOperationException(
                $"Query type '{queryType.Name}' does not implement IMultiProjectionListQuery<,,>");
        }

        // Extract the generic arguments
        var genericArgs = listQueryInterface.GetGenericArguments();
        var projectorType = genericArgs[0];
        var outputType = genericArgs[2];

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
}
