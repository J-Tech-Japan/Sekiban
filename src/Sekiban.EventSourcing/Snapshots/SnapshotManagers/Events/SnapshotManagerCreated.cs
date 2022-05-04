namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events;

public record SnapshotManagerCreated
    (Guid AggregateId, DateTime CreatedAt) : CreateAggregateEvent<SnapshotManager>(
        AggregateId);
