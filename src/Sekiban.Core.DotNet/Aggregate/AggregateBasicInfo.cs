namespace Sekiban.Core.Aggregate;

/// <summary>
///     System use Aggregate Information
///     Application developer usually don't need to use this class.
/// </summary>
public record AggregateBasicInfo
{
    public Guid AggregateId { get; init; } = Guid.Empty;
    public Guid LastEventId { get; init; } = Guid.Empty;
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public int AppliedSnapshotVersion { get; init; } = 0;
    public int Version { get; init; } = 0;
    public string RootPartitionKey { get; init; } = string.Empty;
}
