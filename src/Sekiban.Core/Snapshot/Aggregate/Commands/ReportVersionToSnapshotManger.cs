using Sekiban.Core.Command;

/* Unmerged change from project 'Sekiban.Core (net6.0)'
Before:
using Sekiban.Core.Snapshot.Aggregate;
After:
using Sekiban.Core.Snapshot.Aggregate.Events;
*/
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record ReportVersionToSnapshotManger(
    Guid SnapshotManagerId,
    Type AggregateType,
    Guid TargetAggregateId,
    int Version,
    int? SnapshotVersion) : ICommand<SnapshotManager>
{
    public ReportVersionToSnapshotManger() : this(Guid.Empty, typeof(object), Guid.Empty, 0, null)
    {
    }

    public Guid GetAggregateId() => SnapshotManagerId;
}
