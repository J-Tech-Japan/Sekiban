using Sekiban.Core.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerCreated(DateTime CreatedAt) : IEventPayload<SnapshotManager, SnapshotManagerCreated>
{
    public static SnapshotManager OnEvent(SnapshotManager payload, Event<SnapshotManagerCreated> ev) => new();
}
