using Sekiban.Core.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerCreated(DateTime CreatedAt) : IEventPayload<SnapshotManager>
{
    public SnapshotManager OnEvent(SnapshotManager aggregate, IEvent ev)
    {
        return new();
    }
}
