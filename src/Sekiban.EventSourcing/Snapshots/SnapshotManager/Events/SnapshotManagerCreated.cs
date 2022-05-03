namespace Sekiban.EventSourcing.Snapshots.SnapshotManager.Events;

public record SnapshotManagerCreated
    (Guid AggregateId, DateTime CreatedAt) : CreateAggregateEvent<SnapshotManager>(
        AggregateId);
