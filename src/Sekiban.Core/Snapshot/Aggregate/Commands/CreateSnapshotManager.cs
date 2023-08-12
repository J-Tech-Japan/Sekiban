using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

/// <summary>
///     Snapshot Create Command. This class is internal use for the sekiban.
/// </summary>
public record CreateSnapshotManager : ICommand<SnapshotManager>
{
    public Guid GetAggregateId() => SnapshotManager.SharedId;

    public class Handler(ISekibanDateProducer sekibanDateProducer) : ICommandHandler<SnapshotManager, CreateSnapshotManager>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<SnapshotManager>> HandleCommandAsync(
            Func<AggregateState<SnapshotManager>> getAggregateState,
            CreateSnapshotManager command)
        {
            await Task.CompletedTask;
            yield return new SnapshotManagerCreated(sekibanDateProducer.UtcNow);
        }
    }
}
