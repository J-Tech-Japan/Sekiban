namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events
{
    public record SnapshotManagerCreated(DateTime CreatedAt) : ICreatedEventPayload;
}
