using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Primitives;

/// <summary>
///     Adapter actor for primitive projection runtimes (e.g., WASM).
///     Keeps state inside the runtime and serializes only on persistence.
/// </summary>
public sealed class PrimitiveMultiProjectionActor
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IPrimitiveProjectionHost _host;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger _logger;
    private readonly string _projectorName;

    private IPrimitiveProjectionInstance? _instance;
    private string? _lastSortableUniqueId;
    private int _version;

    public PrimitiveMultiProjectionActor(
        DcbDomainTypes domainTypes,
        IPrimitiveProjectionHost host,
        string projectorName,
        ILogger? logger = null)
    {
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _projectorName = projectorName ?? throw new ArgumentNullException(nameof(projectorName));
        _jsonOptions = domainTypes.JsonSerializerOptions;
        _logger = logger ?? NullLogger.Instance;
    }

    public Task ApplyEventAsync(Event ev)
    {
        var instance = EnsureInstance();
        var payloadJson = JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType(), _jsonOptions);
        instance.ApplyEvent(ev.EventType, payloadJson, ev.Tags, ev.SortableUniqueIdValue);

        _version++;
        if (string.IsNullOrEmpty(_lastSortableUniqueId) ||
            string.Compare(ev.SortableUniqueIdValue, _lastSortableUniqueId, StringComparison.Ordinal) > 0)
        {
            _lastSortableUniqueId = ev.SortableUniqueIdValue;
        }

        return Task.CompletedTask;
    }

    public Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> query) where TResult : notnull
    {
        try
        {
            var instance = EnsureInstance();
            var queryType = query.GetType().FullName ?? query.GetType().Name;
            var queryJson = JsonSerializer.Serialize(query, query.GetType(), _jsonOptions);
            var resultJson = instance.ExecuteQuery(queryType, queryJson);

            var result = JsonSerializer.Deserialize<TResult>(resultJson, _jsonOptions);
            if (result == null)
            {
                return Task.FromResult(ResultBox.Error<TResult>(
                    new InvalidOperationException($"Failed to deserialize query result for {queryType}")));
            }

            return Task.FromResult(ResultBox.FromValue(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ResultBox.Error<TResult>(ex));
        }
    }

    public Task<ResultBox<ListQueryResult<TResult>>> ListQueryAsync<TResult>(IListQueryCommon<TResult> query)
        where TResult : notnull
    {
        try
        {
            var instance = EnsureInstance();
            var queryType = query.GetType().FullName ?? query.GetType().Name;
            var queryJson = JsonSerializer.Serialize(query, query.GetType(), _jsonOptions);
            var resultJson = instance.ExecuteListQuery(queryType, queryJson);

            var result = JsonSerializer.Deserialize<ListQueryResult<TResult>>(resultJson, _jsonOptions);
            if (result == null)
            {
                return Task.FromResult(ResultBox.Error<ListQueryResult<TResult>>(
                    new InvalidOperationException($"Failed to deserialize list query result for {queryType}")));
            }

            return Task.FromResult(ResultBox.FromValue(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ResultBox.Error<ListQueryResult<TResult>>(ex));
        }
    }

    public Task<PrimitiveProjectionSnapshot> CreateSnapshotAsync()
    {
        var instance = EnsureInstance();
        var stateJson = instance.SerializeState();
        var versionResult = _domainTypes.MultiProjectorTypes.GetProjectorVersion(_projectorName);
        var projectorVersion = versionResult.IsSuccess ? versionResult.GetValue() : "unknown";

        return Task.FromResult(
            new PrimitiveProjectionSnapshot(
                _projectorName,
                projectorVersion,
                stateJson,
                _version,
                _lastSortableUniqueId,
                DateTime.UtcNow));
    }

    public Task RestoreSnapshotAsync(PrimitiveProjectionSnapshot snapshot)
    {
        if (!string.Equals(snapshot.ProjectorName, _projectorName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Snapshot projector name mismatch. Expected '{_projectorName}', got '{snapshot.ProjectorName}'.");
        }

        var instance = EnsureInstance();
        instance.RestoreState(snapshot.StateJson);
        _version = snapshot.Version;
        _lastSortableUniqueId = snapshot.LastSortableUniqueId;
        return Task.CompletedTask;
    }

    public Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId)
    {
        if (string.IsNullOrEmpty(sortableUniqueId) || string.IsNullOrEmpty(_lastSortableUniqueId))
        {
            return Task.FromResult(false);
        }

        var result = string.Compare(sortableUniqueId, _lastSortableUniqueId, StringComparison.Ordinal) <= 0;
        return Task.FromResult(result);
    }

    private IPrimitiveProjectionInstance EnsureInstance()
    {
        if (_instance == null)
        {
            _instance = _host.CreateInstance(_projectorName);
            _logger.LogDebug("Primitive projection instance created for {ProjectorName}", _projectorName);
        }

        return _instance;
    }
}
