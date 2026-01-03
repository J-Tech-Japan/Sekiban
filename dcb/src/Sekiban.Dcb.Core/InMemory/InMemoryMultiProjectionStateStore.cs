using System.Collections.Concurrent;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation for tests.
/// </summary>
public sealed class InMemoryMultiProjectionStateStore : IMultiProjectionStateStore
{
    private readonly ConcurrentDictionary<(string ProjectorName, string ProjectorVersion), MultiProjectionStateRecord> _states = new();

    /// <summary>
    ///     Clear all stored states. Used for test isolation.
    /// </summary>
    public void Clear()
    {
        _states.Clear();
    }

    public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        _states.TryGetValue((projectorName, projectorVersion), out var record);
        return Task.FromResult(ResultBox.FromValue(
            record != null ? OptionalValue.FromValue(record) : OptionalValue<MultiProjectionStateRecord>.Empty));
    }

    public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        var record = _states.Values
            .Where(s => s.ProjectorName == projectorName)
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
        var updated = record with { UpdatedAt = DateTime.UtcNow };
        _states[(record.ProjectorName, record.ProjectorVersion)] = updated;
        return Task.FromResult(ResultBox.FromValue(true));
    }

    public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        var list = _states.Values
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
}
