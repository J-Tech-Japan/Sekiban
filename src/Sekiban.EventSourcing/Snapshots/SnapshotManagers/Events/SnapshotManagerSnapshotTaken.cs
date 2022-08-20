namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events
{
    public record SnapshotManagerSnapshotTaken(
        string AggregateTypeName,
        Guid TargetAggregateId,
        int NextSnapshotVersion,
        int? SnapshotVersion) : IChangedEventPayload;
}
