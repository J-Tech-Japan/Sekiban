using System.Collections.Concurrent;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     In-memory implementation of checkpoint manager for development and testing
/// </summary>
public class InMemoryCheckpointManager : ICheckpointManager
{
    private readonly ConcurrentDictionary<string, CheckpointData> _checkpoints = new();

    public Task SaveCheckpointAsync(string subscriptionId, string position, Dictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrEmpty(subscriptionId))
            throw new ArgumentException("Subscription ID cannot be null or empty", nameof(subscriptionId));

        if (string.IsNullOrEmpty(position))
            throw new ArgumentException("Position cannot be null or empty", nameof(position));

        var checkpoint = new CheckpointData(subscriptionId, position, DateTime.UtcNow, metadata);

        _checkpoints[subscriptionId] = checkpoint;
        return Task.CompletedTask;
    }

    public Task<CheckpointData?> LoadCheckpointAsync(string subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
            throw new ArgumentException("Subscription ID cannot be null or empty", nameof(subscriptionId));

        _checkpoints.TryGetValue(subscriptionId, out var checkpoint);
        return Task.FromResult(checkpoint);
    }

    public Task DeleteCheckpointAsync(string subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
            throw new ArgumentException("Subscription ID cannot be null or empty", nameof(subscriptionId));

        _checkpoints.TryRemove(subscriptionId, out _);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<CheckpointData>> ListCheckpointsAsync() =>
        Task.FromResult<IEnumerable<CheckpointData>>(_checkpoints.Values.ToList());
}
