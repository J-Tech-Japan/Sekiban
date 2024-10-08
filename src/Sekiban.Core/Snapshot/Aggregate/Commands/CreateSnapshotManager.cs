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
    public class Handler(ISekibanDateProducer sekibanDateProducer)
        : ICommandHandler<SnapshotManager, CreateSnapshotManager>
    {
        public IEnumerable<IEventPayloadApplicableTo<SnapshotManager>> HandleCommand(
            CreateSnapshotManager command,
            ICommandContext<SnapshotManager> context)
        {
            yield return new SnapshotManagerCreated(sekibanDateProducer.UtcNow);
        }
        public Guid SpecifyAggregateId(CreateSnapshotManager command) => SnapshotManager.SharedId;
    }
}
