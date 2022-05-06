namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;

public record ReportAggregateVersionToSnapshotManger(
    Guid AggregateId,
    Type AggregateType,
    Guid TargetAggregateId,
    int Version,
    int? SnapshotVersion) : ChangeAggregateCommandBase<SnapshotManager>(AggregateId), INoValidateCommand;
public class ReportAggregateVersionToSnapshotMangerHandler : ChangeAggregateCommandHandlerBase<SnapshotManager,
    ReportAggregateVersionToSnapshotManger>
{
    protected override async Task ExecCommandAsync(SnapshotManager aggregate, ReportAggregateVersionToSnapshotManger command)
    {
        aggregate.ReportAggregateVersion(
            command.AggregateId,
            command.AggregateType,
            command.TargetAggregateId,
            command.Version,
            command.SnapshotVersion);
        await Task.CompletedTask;
    }
}
