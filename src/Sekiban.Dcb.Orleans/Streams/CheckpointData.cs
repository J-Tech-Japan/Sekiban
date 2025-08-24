namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Checkpoint data
/// </summary>
public record CheckpointData(
    string SubscriptionId,
    string Position,
    DateTime Timestamp,
    Dictionary<string, string>? Metadata = null);
