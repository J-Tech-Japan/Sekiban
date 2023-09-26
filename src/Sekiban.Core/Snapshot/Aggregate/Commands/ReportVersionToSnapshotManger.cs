using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

/// <summary>
/// </summary>
/// <param name="SnapshotManagerId"></param>
/// <param name="AggregateType"></param>
/// <param name="TargetAggregateId"></param>
/// <param name="Version"></param>
/// <param name="SnapshotVersion"></param>
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

    public class Handler : ICommandHandler<SnapshotManager, ReportVersionToSnapshotManger>
    {
        private readonly IAggregateSettings _aggregateSettings;

        public Handler(IAggregateSettings aggregateSettings) => _aggregateSettings = aggregateSettings;

        public IEnumerable<IEventPayloadApplicableTo<SnapshotManager>> HandleCommand(
            Func<AggregateState<SnapshotManager>> getAggregateState,
            ReportVersionToSnapshotManger command)
        {
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
}
