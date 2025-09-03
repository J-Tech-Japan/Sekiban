using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Interface for managing query types and execution
/// </summary>
public interface IQueryTypes
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
    Task<ResultBox<object>> ExecuteQueryAsync(
        IQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
    IServiceProvider serviceProvider,
    int? safeVersion = null,
    string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null,
    int? unsafeVersion = null);

    /// <summary>
    ///     Execute a list query
    /// </summary>
    Task<ResultBox<object>> ExecuteListQueryAsync(
        IListQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
    IServiceProvider serviceProvider,
    int? safeVersion = null,
    string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null,
    int? unsafeVersion = null);

    /// <summary>
    ///     Execute a list query and return a general result for serialization
    /// </summary>
    Task<ResultBox<ListQueryResultGeneral>> ExecuteListQueryAsGeneralAsync(
        IListQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
    IServiceProvider serviceProvider,
    int? safeVersion = null,
    string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null,
    int? unsafeVersion = null);

    /// <summary>
    ///     Get the multi-projector type for a query
    /// </summary>
    ResultBox<Type> GetMultiProjectorType(IQueryCommon query);

    /// <summary>
    ///     Get the multi-projector type for a list query
    /// </summary>
    ResultBox<Type> GetMultiProjectorType(IListQueryCommon query);

    /// <summary>
    ///     Get a type by its name
    /// </summary>
    Type? GetTypeByName(string typeName);
}
