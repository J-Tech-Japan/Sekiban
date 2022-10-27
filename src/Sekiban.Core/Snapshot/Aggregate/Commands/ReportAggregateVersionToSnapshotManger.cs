using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot.Aggregate.Events;
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
    protected override async IAsyncEnumerable<IChangedEvent<SnapshotManager>> ExecCommandAsync(
        AggregateState<SnapshotManager> aggregateState,
        ReportAggregateVersionToSnapshotManger command)
    {
        await Task.CompletedTask;
        var snapshotFrequency = _aggregateSettings.SnapshotFrequencyForType(command.AggregateType);
        var snapshotOffset = _aggregateSettings.SnapshotOffsetForType(command.AggregateType);

        var nextSnapshotVersion = command.Version / snapshotFrequency * snapshotFrequency;
        var offset = command.Version - nextSnapshotVersion;
        if (nextSnapshotVersion == 0) { yield break; }
        var key = SnapshotManager.SnapshotKey(command.AggregateType.Name, command.TargetAggregateId, nextSnapshotVersion);
        if (!aggregateState.Payload.Requests.Contains(key) && !aggregateState.Payload.RequestTakens.Contains(key))
        {
            yield return new SnapshotManagerRequestAdded(
                command.AggregateType.Name,
                command.TargetAggregateId,
                nextSnapshotVersion,
                command.SnapshotVersion);
        }
        if (aggregateState.Payload.Requests.Contains(key) && !aggregateState.Payload.RequestTakens.Contains(key) && offset > snapshotOffset)
        {
            yield return new SnapshotManagerSnapshotTaken(
                command.AggregateType.Name,
                command.TargetAggregateId,
                nextSnapshotVersion,
                command.SnapshotVersion);
        }
    }
}
