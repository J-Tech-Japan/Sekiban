namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Interface for managing subscription checkpoints
/// </summary>
public interface ICheckpointManager
{
    /// <summary>
    ///     Save a checkpoint for a subscription
    /// </summary>
    /// <param name="subscriptionId">Subscription identifier</param>
    /// <param name="position">Position to checkpoint</param>
    /// <param name="metadata">Optional metadata to store with checkpoint</param>
    Task SaveCheckpointAsync(string subscriptionId, string position, Dictionary<string, string>? metadata = null);

    /// <summary>
    ///     Load the last checkpoint for a subscription
    /// </summary>
    /// <param name="subscriptionId">Subscription identifier</param>
    /// <returns>The last checkpointed position, or null if no checkpoint exists</returns>
    Task<CheckpointData?> LoadCheckpointAsync(string subscriptionId);

    /// <summary>
    ///     Delete a checkpoint for a subscription
    /// </summary>
    /// <param name="subscriptionId">Subscription identifier</param>
    Task DeleteCheckpointAsync(string subscriptionId);

    /// <summary>
    ///     List all checkpoints
    /// </summary>
    Task<IEnumerable<CheckpointData>> ListCheckpointsAsync();
}
