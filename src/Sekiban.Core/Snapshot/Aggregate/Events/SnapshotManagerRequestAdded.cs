using Sekiban.Core.Event;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerRequestAdded(
    string AggregateTypeName,
    Guid TargetAggregateId,
    int NextSnapshotVersion,
    int? SnapshotVersion) : IEventPayload<SnapshotManager>
{
    public SnapshotManager OnEvent(SnapshotManager payload, IEvent ev) => payload with
    {
        Requests = payload.Requests.Add(SnapshotManager.SnapshotKey(AggregateTypeName, TargetAggregateId, NextSnapshotVersion))
    };
}
