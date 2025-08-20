using System.Reflection;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Queries;

/// <summary>
/// Simple implementation of IQueryTypes using reflection
/// </summary>
public class SimpleQueryTypes : IQueryTypes
{
    private readonly Dictionary<Type, Type> _queryToProjectorMap = new();
    private readonly Dictionary<Type, Type> _queryToOutputMap = new();
    private readonly Dictionary<string, Type> _typeNameMap = new();
    private readonly List<Type> _queryTypes = new();
    private readonly List<Type> _responseTypes = new();
    
    public SimpleQueryTypes(params Assembly[] assemblies)
    {
        if (assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetExecutingAssembly() };
        }
        
        ScanAssemblies(assemblies);
    }
    
    private void ScanAssemblies(Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .ToList();
            
            // Find all query implementations
            foreach (var type in types)
            {
                // Check for IMultiProjectionQuery implementations
                var queryInterface = type.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && 
                        i.GetGenericTypeDefinition() == typeof(IMultiProjectionQuery<,,>));
                
                if (queryInterface != null)
                {
                    var genericArgs = queryInterface.GetGenericArguments();
                    var projectorType = genericArgs[0];
                    var outputType = genericArgs[2];
                    
                    _queryTypes.Add(type);
                    _responseTypes.Add(outputType);
                    _queryToProjectorMap[type] = projectorType;
                    _queryToOutputMap[type] = outputType;
                    _typeNameMap[type.Name] = type;
                    _typeNameMap[type.FullName ?? type.Name] = type;
                }
                
                // Check for IMultiProjectionListQuery implementations
                var listQueryInterface = type.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && 
                        i.GetGenericTypeDefinition() == typeof(IMultiProjectionListQuery<,,>));
                
                if (listQueryInterface != null)
                {
                    var genericArgs = listQueryInterface.GetGenericArguments();
                    var projectorType = genericArgs[0];
                    var outputType = genericArgs[2];
                    
                    _queryTypes.Add(type);
                    _responseTypes.Add(outputType);
                    _queryToProjectorMap[type] = projectorType;
                    _queryToOutputMap[type] = outputType;
                    _typeNameMap[type.Name] = type;
                    _typeNameMap[type.FullName ?? type.Name] = type;
                }
            }
        }
    }
    
    public IEnumerable<Type> GetQueryTypes() => _queryTypes;
    
    public IEnumerable<Type> GetQueryResponseTypes() => _responseTypes.Distinct();
    
    public async Task<ResultBox<object>> ExecuteQueryAsync(
        IQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
        IServiceProvider serviceProvider)
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
        var handleMethod = queryType.GetMethod("HandleQuery", 
            BindingFlags.Public | BindingFlags.Static);
        
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
        var context = new QueryContext(serviceProvider);
        
        // Invoke the static method
        try
        {
            var result = handleMethod.Invoke(null, new object[] { projector, query, context });
            
            // Check if result is a ResultBox<T>
            if (result != null && result.GetType().IsGenericType && 
                result.GetType().GetGenericTypeDefinition().Name == "ResultBox`1")
            {
                var isSuccess = result.GetType().GetProperty("IsSuccess")?.GetValue(result) as bool?;
                
                if (isSuccess == true)
                {
                    var getValue = result.GetType().GetMethod("GetValue");
                    var value = getValue?.Invoke(result, null);
                    return ResultBox.FromValue<object>(value!);
                }
                else
                {
                    var getException = result.GetType().GetMethod("GetException");
                    var exception = getException?.Invoke(result, null) as Exception;
                    return ResultBox.Error<object>(exception ?? new Exception("Query execution failed"));
                }
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
        IServiceProvider serviceProvider)
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
        var context = new QueryContext(serviceProvider);
        
        // Get HandleFilter method
        var handleFilterMethod = queryType.GetMethod("HandleFilter", 
            BindingFlags.Public | BindingFlags.Static);
        
        if (handleFilterMethod == null)
        {
            return ResultBox.Error<object>(
                new InvalidOperationException($"List query type {queryType.Name} does not have HandleFilter method"));
        }
        
        // Get HandleSort method
        var handleSortMethod = queryType.GetMethod("HandleSort", 
            BindingFlags.Public | BindingFlags.Static);
        
        if (handleSortMethod == null)
        {
            return ResultBox.Error<object>(
                new InvalidOperationException($"List query type {queryType.Name} does not have HandleSort method"));
        }
        
        try
        {
            // Execute filter
            var filterResult = handleFilterMethod.Invoke(null, new object[] { projector, query, context });
            if (filterResult == null || !filterResult.GetType().IsGenericType ||
                filterResult.GetType().GetGenericTypeDefinition().Name != "ResultBox`1")
            {
                return ResultBox.Error<object>(new Exception("Filter execution failed - invalid result type"));
            }
            
            var filterIsSuccess = filterResult.GetType().GetProperty("IsSuccess")?.GetValue(filterResult) as bool?;
            if (filterIsSuccess != true)
            {
                var filterException = filterResult.GetType().GetMethod("GetException")?.Invoke(filterResult, null) as Exception;
                return ResultBox.Error<object>(filterException ?? new Exception("Filter execution failed"));
            }
            
            var getFilterValue = filterResult.GetType().GetMethod("GetValue");
            var filteredItems = getFilterValue?.Invoke(filterResult, null);
            
            // Execute sort
            var sortResult = handleSortMethod.Invoke(null, new object[] { filteredItems, query, context });
            if (sortResult == null || !sortResult.GetType().IsGenericType ||
                sortResult.GetType().GetGenericTypeDefinition().Name != "ResultBox`1")
            {
                return ResultBox.Error<object>(new Exception("Sort execution failed - invalid result type"));
            }
            
            var sortIsSuccess = sortResult.GetType().GetProperty("IsSuccess")?.GetValue(sortResult) as bool?;
            if (sortIsSuccess != true)
            {
                var sortException = sortResult.GetType().GetMethod("GetException")?.Invoke(sortResult, null) as Exception;
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
                
                var result = createPaginatedMethod?.Invoke(null, new object[] { pagingParam, list });
                return ResultBox.FromValue<object>(result!);
            }
            else
            {
                var items = sortedItems?.ToList() ?? new List<object>();
                var resultType = typeof(ListQueryResult<>).MakeGenericType(outputType);
                var result = Activator.CreateInstance(resultType, 
                    items.Count, null, null, null, items);
                return ResultBox.FromValue<object>(result!);
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<object>(ex);
        }
    }
    
    public ResultBox<Type> GetMultiProjectorType(IQueryCommon query)
    {
        var queryType = query.GetType();
        
        if (_queryToProjectorMap.TryGetValue(queryType, out var projectorType))
        {
            return ResultBox.FromValue(projectorType);
        }
        
        return ResultBox.Error<Type>(
            new InvalidOperationException($"Query type {queryType.Name} is not registered"));
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
    
    public Type? GetTypeByName(string typeName)
    {
        return _typeNameMap.TryGetValue(typeName, out var type) ? type : null;
    }
}