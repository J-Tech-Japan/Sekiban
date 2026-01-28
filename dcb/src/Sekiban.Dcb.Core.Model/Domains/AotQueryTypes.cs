using ResultBoxes;
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
    public Task<ResultBox<object>> ExecuteQueryAsync(
        IQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        return Task.FromResult(
            ResultBox.Error<object>(new NotSupportedException("Query execution must be registered explicitly in AOT mode")));
    }

    /// <inheritdoc />
    public Task<ResultBox<object>> ExecuteListQueryAsync(
        IListQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        return Task.FromResult(
            ResultBox.Error<object>(new NotSupportedException("List query execution must be registered explicitly in AOT mode")));
    }

    /// <inheritdoc />
    public Task<ResultBox<ListQueryResultGeneral>> ExecuteListQueryAsGeneralAsync(
        IListQueryCommon query,
        Func<Task<ResultBox<IMultiProjectionPayload>>> projectorProvider,
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        return Task.FromResult(
            ResultBox.Error<ListQueryResultGeneral>(new NotSupportedException("List query execution must be registered explicitly in AOT mode")));
    }

    /// <inheritdoc />
    public ResultBox<Type> GetMultiProjectorType(IQueryCommon query) =>
        ResultBox.Error<Type>(new NotSupportedException("GetMultiProjectorType is not supported in AOT mode"));

    /// <inheritdoc />
    public ResultBox<Type> GetMultiProjectorType(IListQueryCommon query) =>
        ResultBox.Error<Type>(new NotSupportedException("GetMultiProjectorType is not supported in AOT mode"));

    /// <inheritdoc />
    public Type? GetTypeByName(string typeName) => null;
}
