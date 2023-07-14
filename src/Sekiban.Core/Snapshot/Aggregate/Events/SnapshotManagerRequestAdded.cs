using Sekiban.Core.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerRequestAdded(
    string AggregateTypeName,
    Guid TargetAggregateId,
    int NextSnapshotVersion,
    int? SnapshotVersion) : IEventPayload<SnapshotManager, SnapshotManagerRequestAdded>
{

    public static SnapshotManager OnEvent(SnapshotManager payload, Event<SnapshotManagerRequestAdded> ev) =>
        payload with
        {
            Requests = payload.Requests.Add(
                SnapshotManager.SnapshotKey(ev.Payload.AggregateTypeName, ev.Payload.TargetAggregateId, ev.Payload.NextSnapshotVersion))
        };
}
