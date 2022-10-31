using Sekiban.Core.Event;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerCreated(DateTime CreatedAt) : ICreatedEvent<SnapshotManager>
{
    public SnapshotManager OnEvent(SnapshotManager aggregate, IEvent @event) => new SnapshotManager();
}
