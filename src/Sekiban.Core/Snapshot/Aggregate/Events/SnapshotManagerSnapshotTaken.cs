using Sekiban.Core.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

/// <summary>
///     Snapshot Manager Snapshot Taken Event. This class is internal use for the sekiban.
/// </summary>
/// <param name="AggregateTypeName"></param>
/// <param name="TargetAggregateId"></param>
/// <param name="NextSnapshotVersion"></param>
/// <param name="SnapshotVersion"></param>
public record SnapshotManagerSnapshotTaken(
    string AggregateTypeName,
    Guid TargetAggregateId,
    int NextSnapshotVersion,
    int? SnapshotVersion) : IEventPayload<SnapshotManager, SnapshotManagerSnapshotTaken>
{
    public static SnapshotManager OnEvent(SnapshotManager payload, Event<SnapshotManagerSnapshotTaken> ev) =>
        payload with
        {
            Requests = payload.Requests.Remove(
                SnapshotManager.SnapshotKey(ev.Payload.AggregateTypeName, ev.Payload.TargetAggregateId, ev.Payload.NextSnapshotVersion)),
            RequestTakens = payload.RequestTakens.Add(
                SnapshotManager.SnapshotKey(ev.Payload.AggregateTypeName, ev.Payload.TargetAggregateId, ev.Payload.NextSnapshotVersion))
        };
}
