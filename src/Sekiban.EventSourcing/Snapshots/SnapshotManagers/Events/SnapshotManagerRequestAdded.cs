namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events
{
    public record SnapshotManagerRequestAdded(
        string AggregateTypeName,
        Guid TargetAggregateId,
        int NextSnapshotVersion,
        int? SnapshotVersion) : IChangedEventPayload;
}
