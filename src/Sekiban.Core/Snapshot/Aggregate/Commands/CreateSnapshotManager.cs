using Sekiban.Core.Command;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record CreateSnapshotManager : ICommand<SnapshotManager>
{
    public Guid GetAggregateId() => SnapshotManager.SharedId;
}
