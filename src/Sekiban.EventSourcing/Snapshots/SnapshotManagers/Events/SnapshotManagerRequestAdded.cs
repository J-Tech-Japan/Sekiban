namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events;

public record SnapshotManagerRequestAdded(
    Guid AggregateId,
    string AggregateTypeName,
    Guid TargetAggregateId,
    int NextSnapshotVersion,
    int? SnapshotVersion) : ChangeAggregateEvent<SnapshotManager>(AggregateId);
