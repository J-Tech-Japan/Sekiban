using Sekiban.Core.Event;
using Sekiban.Core.Shared;
using System.Collections.Immutable;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerCreated(DateTime CreatedAt) : ICreatedEvent<SnapshotManagerPayload>
{
    public SnapshotManagerPayload OnEvent(SnapshotManagerPayload aggregatePayload, IAggregateEvent aggregateEvent)
    {
        return new SnapshotManagerPayload();
    }
}
