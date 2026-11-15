using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     AOT-compatible implementation of ICoreQueryTypes.
///     This is a stub implementation that can be extended for specific query patterns.
/// </summary>
public sealed class AotQueryTypes : ICoreQueryTypes
{
    private readonly Dictionary<string, Type> _queryTypes = new();
    private readonly Dictionary<string, Type> _queryResponseTypes = new();

    /// <inheritdoc />
    public IEnumerable<Type> GetQueryTypes() => _queryTypes.Values;

    /// <inheritdoc />
    public IEnumerable<Type> GetQueryResponseTypes() => _queryResponseTypes.Values;

    /// <inheritdoc />
    public Task<object> ExecuteQueryAsync(
        IQueryCommon query,
        Func<Task<IMultiProjectionPayload>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        return Task.FromException<object>(
            new NotSupportedException("Query execution must be registered explicitly in AOT mode"));
    }

    /// <inheritdoc />
    public Task<object> ExecuteListQueryAsync(
        IListQueryCommon query,
        Func<Task<IMultiProjectionPayload>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        return Task.FromException<object>(
            new NotSupportedException("List query execution must be registered explicitly in AOT mode"));
    }

    /// <inheritdoc />
    public Task<ListQueryResultGeneral> ExecuteListQueryAsGeneralAsync(
        IListQueryCommon query,
        Func<Task<IMultiProjectionPayload>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        return Task.FromException<ListQueryResultGeneral>(
            new NotSupportedException("List query execution must be registered explicitly in AOT mode"));
    }

    /// <inheritdoc />
    public Type GetMultiProjectorType(IQueryCommon query) =>
        throw new NotSupportedException("GetMultiProjectorType is not supported in AOT mode");

    /// <inheritdoc />
    public Type GetMultiProjectorType(IListQueryCommon query) =>
        throw new NotSupportedException("GetMultiProjectorType is not supported in AOT mode");

    /// <inheritdoc />
    public Type? GetTypeByName(string typeName) => null;
}
