using Sekiban.Core.Event;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerSnapshotTaken(
    string AggregateTypeName,
    Guid TargetAggregateId,
    int NextSnapshotVersion,
    int? SnapshotVersion) : IApplicableEvent<SnapshotManager>
{

    public SnapshotManager OnEvent(SnapshotManager payload, IEvent ev) => payload with
    {
        Requests = payload.Requests.Remove(SnapshotManager.SnapshotKey(AggregateTypeName, TargetAggregateId, NextSnapshotVersion)),
        RequestTakens = payload.RequestTakens.Add(SnapshotManager.SnapshotKey(AggregateTypeName, TargetAggregateId, NextSnapshotVersion))
    };
}
