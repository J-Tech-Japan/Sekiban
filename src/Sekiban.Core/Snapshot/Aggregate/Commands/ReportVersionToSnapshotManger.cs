using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record ReportVersionToSnapshotManger(
    Guid SnapshotManagerId,
    Type AggregateType,
    Guid TargetAggregateId,
    int Version,
    int? SnapshotVersion) : ChangeCommandBase<SnapshotManager>, INoValidateCommand
{
    public ReportVersionToSnapshotManger() : this(Guid.Empty, typeof(object), Guid.Empty, 0, null) { }
    public override Guid GetAggregateId() => SnapshotManagerId;
}
public class ReportVersionToSnapshotMangerHandler : ChangeCommandHandlerBase<SnapshotManager,
    ReportVersionToSnapshotManger>
{
    private readonly IAggregateSettings _aggregateSettings;
    public ReportVersionToSnapshotMangerHandler(IAggregateSettings aggregateSettings) => _aggregateSettings = aggregateSettings;
    protected override async IAsyncEnumerable<IChangedEvent<SnapshotManager>> ExecCommandAsync(
        Func<AggregateIdentifierState<SnapshotManager>> getAggregateState,
        ReportVersionToSnapshotManger command)
    {
        await Task.CompletedTask;
        var snapshotFrequency = _aggregateSettings.SnapshotFrequencyForType(command.AggregateType);
        var snapshotOffset = _aggregateSettings.SnapshotOffsetForType(command.AggregateType);

        var nextSnapshotVersion = command.Version / snapshotFrequency * snapshotFrequency;
        var offset = command.Version - nextSnapshotVersion;
        if (nextSnapshotVersion == 0) { yield break; }
        var key = SnapshotManager.SnapshotKey(command.AggregateType.Name, command.TargetAggregateId, nextSnapshotVersion);
        if (!getAggregateState().Payload.Requests.Contains(key) && !getAggregateState().Payload.RequestTakens.Contains(key))
        {
            yield return new SnapshotManagerRequestAdded(
                command.AggregateType.Name,
                command.TargetAggregateId,
                nextSnapshotVersion,
                command.SnapshotVersion);
        }
        if (getAggregateState().Payload.Requests.Contains(key) &&
            !getAggregateState().Payload.RequestTakens.Contains(key) &&
            offset > snapshotOffset)
        {
            yield return new SnapshotManagerSnapshotTaken(
                command.AggregateType.Name,
                command.TargetAggregateId,
                nextSnapshotVersion,
                command.SnapshotVersion);
        }
    }
}