using Sekiban.Core.Event;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerSnapshotTaken(
    string AggregateTypeName,
    Guid TargetAggregateId,
    int NextSnapshotVersion,
    int? SnapshotVersion) : IChangedEvent<SnapshotManagerPayload>
{

    public SnapshotManagerPayload OnEvent(SnapshotManagerPayload payload, IAggregateEvent aggregateEvent)
    {
        return payload with
        {
            Requests = payload.Requests.Remove(SnapshotManagerPayload.SnapshotKey(AggregateTypeName, TargetAggregateId, NextSnapshotVersion)),
            RequestTakens = payload.RequestTakens.Add(SnapshotManagerPayload.SnapshotKey(AggregateTypeName, TargetAggregateId, NextSnapshotVersion))
        };
    }
}
