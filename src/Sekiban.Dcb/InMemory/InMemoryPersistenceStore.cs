using Sekiban.Dcb.Actors;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory persistence store for testing projection persistence
/// </summary>
public class InMemoryPersistenceStore
{
    private readonly ConcurrentDictionary<string, SerializedProjectionState> _store = new();
    private readonly ConcurrentDictionary<string, List<SerializedProjectionState>> _history = new();

    /// <summary>
    ///     Save projection state
    /// </summary>
    public Task<bool> SaveAsync(string projectorName, SerializedProjectionState state)
    {
        try
        {
            _store[projectorName] = state;

            // Keep history for testing
            if (!_history.ContainsKey(projectorName))
            {
                _history[projectorName] = new List<SerializedProjectionState>();
            }
            _history[projectorName].Add(state);

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    ///     Load projection state
    /// </summary>
    public Task<SerializedProjectionState?> LoadAsync(string projectorName)
    {
        return Task.FromResult(_store.TryGetValue(projectorName, out var state) ? state : null);
    }

    /// <summary>
    ///     Delete projection state
    /// </summary>
    public Task<bool> DeleteAsync(string projectorName)
    {
        return Task.FromResult(_store.TryRemove(projectorName, out _));
    }

    /// <summary>
    ///     Check if projection exists
    /// </summary>
    public Task<bool> ExistsAsync(string projectorName)
    {
        return Task.FromResult(_store.ContainsKey(projectorName));
    }

    /// <summary>
    ///     Get all saved projections
    /// </summary>
    public Task<IReadOnlyDictionary<string, SerializedProjectionState>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyDictionary<string, SerializedProjectionState>>(_store);
    }

    /// <summary>
    ///     Get save history for testing
    /// </summary>
    public Task<IReadOnlyList<SerializedProjectionState>> GetHistoryAsync(string projectorName)
    {
        if (_history.TryGetValue(projectorName, out var history))
        {
            return Task.FromResult<IReadOnlyList<SerializedProjectionState>>(history);
        }
        return Task.FromResult<IReadOnlyList<SerializedProjectionState>>(new List<SerializedProjectionState>());
    }

    /// <summary>
    ///     Clear all data
    /// </summary>
    public Task ClearAsync()
    {
        _store.Clear();
        _history.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Get statistics for testing
    /// </summary>
    public Task<PersistenceStatistics> GetStatisticsAsync()
    {
        var stats = new PersistenceStatistics(
            TotalProjections: _store.Count,
            TotalSaves: _history.Values.Sum(h => h.Count),
            ProjectionSizes: _store.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Payload.Length));

        return Task.FromResult(stats);
    }
}

/// <summary>
///     Statistics for testing
/// </summary>
public record PersistenceStatistics(
    int TotalProjections,
    int TotalSaves,
    Dictionary<string, int> ProjectionSizes);