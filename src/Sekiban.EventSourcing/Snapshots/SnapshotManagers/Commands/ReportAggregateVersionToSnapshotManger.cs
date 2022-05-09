using Sekiban.EventSourcing.Settings;
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
    private readonly IAggregateSettings _aggregateSettings;
    public ReportAggregateVersionToSnapshotMangerHandler(IAggregateSettings aggregateSettings) =>
        _aggregateSettings = aggregateSettings;

    protected override async Task ExecCommandAsync(SnapshotManager aggregate, ReportAggregateVersionToSnapshotManger command)
    {
        var snapshotFrequency = _aggregateSettings.SnapshotFrequencyForType(command.AggregateType);
        var snapshotOffset = _aggregateSettings.SnapshotOffsetForType(command.AggregateType);

        aggregate.ReportAggregateVersion(
            command.AggregateId,
            command.AggregateType,
            command.TargetAggregateId,
            command.Version,
            command.SnapshotVersion,
            snapshotFrequency,
            snapshotOffset);
        await Task.CompletedTask;
    }
}
