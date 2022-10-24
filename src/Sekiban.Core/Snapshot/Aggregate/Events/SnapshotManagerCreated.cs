using Sekiban.Core.Event;
using Sekiban.Core.Shared;
using System.Collections.Immutable;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerCreated(DateTime CreatedAt) : ICreatedEvent<SnapshotManager>
{
    public SnapshotManager OnEvent(SnapshotManager aggregate, IAggregateEvent aggregateEvent)
    {
        return new SnapshotManager();
    }
}
