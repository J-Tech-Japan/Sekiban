using Sekiban.Core.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

/// <summary>
///     SnapshotManager Created Event. This class is internal use for the sekiban.
/// </summary>
/// <param name="CreatedAt"></param>
public record SnapshotManagerCreated(DateTime CreatedAt) : IEventPayload<SnapshotManager, SnapshotManagerCreated>
{
    public static SnapshotManager OnEvent(SnapshotManager payload, Event<SnapshotManagerCreated> ev) => new();
}
