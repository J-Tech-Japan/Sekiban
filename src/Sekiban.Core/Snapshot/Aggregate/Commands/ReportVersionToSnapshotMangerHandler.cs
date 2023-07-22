using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public class ReportVersionToSnapshotMangerHandler : ICommandHandler<SnapshotManager, ReportVersionToSnapshotManger>
{
    private readonly IAggregateSettings _aggregateSettings;

    public ReportVersionToSnapshotMangerHandler(IAggregateSettings aggregateSettings) => _aggregateSettings = aggregateSettings;

    public async IAsyncEnumerable<IEventPayloadApplicableTo<SnapshotManager>> HandleCommandAsync(
        Func<AggregateState<SnapshotManager>> getAggregateState,
        ReportVersionToSnapshotManger command)
    {
        await Task.CompletedTask;
        var snapshotFrequency = _aggregateSettings.SnapshotFrequencyForType(command.AggregateType);
        var snapshotOffset = _aggregateSettings.SnapshotOffsetForType(command.AggregateType);

        var nextSnapshotVersion = command.Version / snapshotFrequency * snapshotFrequency;
        var offset = command.Version - nextSnapshotVersion;
        if (nextSnapshotVersion == 0)
        {
            yield break;
        }
        var key = SnapshotManager.SnapshotKey(command.AggregateType.Name, command.TargetAggregateId, nextSnapshotVersion);
        if (!getAggregateState().Payload.Requests.Contains(key) && !getAggregateState().Payload.RequestTakens.Contains(key))
        {
            yield return new SnapshotManagerRequestAdded(
                command.AggregateType.Name,
                command.TargetAggregateId,
                nextSnapshotVersion,
                command.SnapshotVersion);
        }
        if (getAggregateState().Payload.Requests.Contains(key) && !getAggregateState().Payload.RequestTakens.Contains(key) && offset > snapshotOffset)
        {
            yield return new SnapshotManagerSnapshotTaken(
                command.AggregateType.Name,
                command.TargetAggregateId,
                nextSnapshotVersion,
                command.SnapshotVersion);
        }
    }
}
