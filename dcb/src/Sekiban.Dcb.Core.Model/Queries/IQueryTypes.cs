using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Core interface for managing query types and execution. Failures are thrown as exceptions.
/// </summary>
public interface ICoreQueryTypes
{
    /// <summary>
    ///     Get all registered query types
    /// </summary>
    IEnumerable<Type> GetQueryTypes();

    /// <summary>
    ///     Get all query response types
    /// </summary>
    IEnumerable<Type> GetQueryResponseTypes();

    /// <summary>
    ///     Execute a single-result query
    /// </summary>
    Task<object> ExecuteQueryAsync(
        IQueryCommon query,
        Func<Task<IMultiProjectionPayload>> projectorProvider,
    IServiceProvider serviceProvider,
    int? safeVersion = null,
    string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null,
    int? unsafeVersion = null);

    /// <summary>
    ///     Execute a list query
    /// </summary>
    Task<object> ExecuteListQueryAsync(
        IListQueryCommon query,
        Func<Task<IMultiProjectionPayload>> projectorProvider,
    IServiceProvider serviceProvider,
    int? safeVersion = null,
    string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null,
    int? unsafeVersion = null);

    /// <summary>
    ///     Execute a list query and return a general result for serialization
    /// </summary>
    Task<ListQueryResultGeneral> ExecuteListQueryAsGeneralAsync(
        IListQueryCommon query,
        Func<Task<IMultiProjectionPayload>> projectorProvider,
    IServiceProvider serviceProvider,
    int? safeVersion = null,
    string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null,
    int? unsafeVersion = null);

    /// <summary>
    ///     Get the multi-projector type for a query
    /// </summary>
    Type GetMultiProjectorType(IQueryCommon query);

    /// <summary>
    ///     Get the multi-projector type for a list query
    /// </summary>
    Type GetMultiProjectorType(IListQueryCommon query);

    /// <summary>
    ///     Get a type by its name
    /// </summary>
    Type? GetTypeByName(string typeName);
}
