namespace Sekiban.EventSourcing.Snapshots;

public record SnapshotListIndex(Guid AggregateId, Guid SnapshotId, bool IsDeleted, int Version, string PartitionKey);
