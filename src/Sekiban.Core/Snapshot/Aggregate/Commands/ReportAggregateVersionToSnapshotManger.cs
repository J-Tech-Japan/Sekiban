using Sekiban.Core.Command;
using Sekiban.Core.Setting;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record ReportAggregateVersionToSnapshotManger(
    Guid SnapshotManagerId,
    Type AggregateType,
    Guid TargetAggregateId,
    int Version,
    int? SnapshotVersion) : ChangeAggregateCommandBase<SnapshotManager>, INoValidateCommand
{
    public ReportAggregateVersionToSnapshotManger() : this(Guid.Empty, typeof(object), Guid.Empty, 0, null) { }
    public override Guid GetAggregateId()
    {
        return SnapshotManagerId;
    }
}
public class ReportAggregateVersionToSnapshotMangerHandler : ChangeAggregateCommandHandlerBase<SnapshotManager,
    ReportAggregateVersionToSnapshotManger>
{
    private readonly IAggregateSettings _aggregateSettings;
    public ReportAggregateVersionToSnapshotMangerHandler(IAggregateSettings aggregateSettings)
    {
        _aggregateSettings = aggregateSettings;
    }

    protected override async Task ExecCommandAsync(SnapshotManager aggregate, ReportAggregateVersionToSnapshotManger command)
    {
        var snapshotFrequency = _aggregateSettings.SnapshotFrequencyForType(command.AggregateType);
        var snapshotOffset = _aggregateSettings.SnapshotOffsetForType(command.AggregateType);

        aggregate.ReportAggregateVersion(
            command.AggregateType,
            command.TargetAggregateId,
            command.Version,
            command.SnapshotVersion,
            snapshotFrequency,
            snapshotOffset);
        await Task.CompletedTask;
    }
}