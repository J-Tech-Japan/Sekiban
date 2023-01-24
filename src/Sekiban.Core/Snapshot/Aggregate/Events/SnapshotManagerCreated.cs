using Sekiban.Core.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerCreated(DateTime CreatedAt) : IEventPayload<SnapshotManager, SnapshotManagerCreated>
{
    public SnapshotManager OnEventInstance(SnapshotManager payload, Event<SnapshotManagerCreated> ev) => new();
    public static SnapshotManager OnEvent(SnapshotManager payload, Event<SnapshotManagerCreated> ev) => new();
}
