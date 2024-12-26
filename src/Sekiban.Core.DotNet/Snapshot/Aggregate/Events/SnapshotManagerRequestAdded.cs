using Sekiban.Core.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

/// <summary>
///     Snapshot Manager Request Added Event. This class is internal use for the sekiban.
/// </summary>
/// <param name="AggregateTypeName"></param>
/// <param name="TargetAggregateId"></param>
/// <param name="NextSnapshotVersion"></param>
/// <param name="SnapshotVersion"></param>
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
                SnapshotManager.SnapshotKey(
                    ev.Payload.AggregateTypeName,
                    ev.Payload.TargetAggregateId,
                    ev.Payload.NextSnapshotVersion))
        };
}
