using System.Collections.Concurrent;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation for tests.
/// </summary>
public sealed class InMemoryMultiProjectionStateStore : IMultiProjectionStateStore
{
    private readonly ConcurrentDictionary<(string ServiceId, string ProjectorName, string ProjectorVersion), MultiProjectionStateRecord> _states = new();
    private readonly ConcurrentDictionary<(string ServiceId, string ProjectorName, string ProjectorVersion), byte[]> _stateData = new();
    private readonly IServiceIdProvider _serviceIdProvider;

    public InMemoryMultiProjectionStateStore(IServiceIdProvider? serviceIdProvider = null)
    {
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
    }

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    /// <summary>
    ///     Clear all stored states. Used for test isolation.
    /// </summary>
    public void Clear()
    {
        var serviceId = CurrentServiceId;
        var keysToRemove = _states.Keys.Where(k => k.ServiceId == serviceId).ToList();
        foreach (var key in keysToRemove)
        {
            _states.TryRemove(key, out _);
            _stateData.TryRemove(key, out _);
        }
    }

    public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        _states.TryGetValue((serviceId, projectorName, projectorVersion), out var record);
        return Task.FromResult(ResultBox.FromValue(
            record != null ? OptionalValue.FromValue(record) : OptionalValue<MultiProjectionStateRecord>.Empty));
    }

    public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        var record = _states
            .Where(kvp => kvp.Key.ServiceId == serviceId && kvp.Value.ProjectorName == projectorName)
            .Select(kvp => kvp.Value)
            .OrderByDescending(s => s.EventsProcessed)
            .ThenByDescending(s => s.LastSortableUniqueId)
            .FirstOrDefault();

        return Task.FromResult(ResultBox.FromValue(
            record != null ? OptionalValue.FromValue(record) : OptionalValue<MultiProjectionStateRecord>.Empty));
    }

    public Task<ResultBox<bool>> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ResultBox.Error<bool>(
            new NotSupportedException(
                "InMemoryMultiProjectionStateStore requires payload stream upsert. Use UpsertFromStreamAsync.")));
    }

    public async Task<ResultBox<bool>> UpsertFromStreamAsync(
        MultiProjectionStateWriteRequest request,
        Stream stream,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var bytes = ms.ToArray();

        var serviceId = CurrentServiceId;
        var key = (serviceId, request.ProjectorName, request.ProjectorVersion);
        _states[key] = request.ToRecord();
        _stateData[key] = bytes;
        return ResultBox.FromValue(true);
    }

    public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(
        MultiProjectionStateRecord record,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        var key = (serviceId, record.ProjectorName, record.ProjectorVersion);
        if (_stateData.TryGetValue(key, out var data))
        {
            return Task.FromResult(ResultBox.FromValue<Stream>(new MemoryStream(data, writable: false)));
        }

        return Task.FromResult(ResultBox.Error<Stream>(
            new InvalidOperationException(
                $"InMemory snapshot payload not found for {record.ProjectorName}/{record.ProjectorVersion}")));
    }

    public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        var list = _states
            .Where(kvp => kvp.Key.ServiceId == serviceId)
            .Select(kvp => kvp.Value)
            .Select(s => new ProjectorStateInfo(
                s.ProjectorName,
                s.ProjectorVersion,
                s.EventsProcessed,
                s.UpdatedAt,
                s.OriginalSizeBytes,
                s.CompressedSizeBytes,
                s.LastSortableUniqueId))
            .ToList();

        return Task.FromResult(ResultBox.FromValue<IReadOnlyList<ProjectorStateInfo>>(list));
    }

    public Task<ResultBox<bool>> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        var removed = _states.TryRemove((serviceId, projectorName, projectorVersion), out _);
        return Task.FromResult(ResultBox.FromValue(removed));
    }

    public Task<ResultBox<int>> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        if (string.IsNullOrEmpty(projectorName))
        {
            var keysToRemove = _states.Keys.Where(k => k.ServiceId == serviceId).ToList();
            foreach (var key in keysToRemove)
            {
                _states.TryRemove(key, out _);
                _stateData.TryRemove(key, out _);
            }
            var count = keysToRemove.Count;
            return Task.FromResult(ResultBox.FromValue(count));
        }
        else
        {
            var keysToRemove = _states.Keys
                .Where(k => k.ServiceId == serviceId && k.ProjectorName == projectorName)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _states.TryRemove(key, out _);
                _stateData.TryRemove(key, out _);
            }

            return Task.FromResult(ResultBox.FromValue(keysToRemove.Count));
        }
    }
}
