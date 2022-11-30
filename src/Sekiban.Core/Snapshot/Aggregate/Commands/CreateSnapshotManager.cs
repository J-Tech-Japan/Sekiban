using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record CreateSnapshotManager : ICommandBase<SnapshotManager>
{
    public Guid GetAggregateId() => Aggregate.SnapshotManager.SharedId;
}
public class CreateSnapshotManagerHandler : ICommandHandlerBase<SnapshotManager, CreateSnapshotManager>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public CreateSnapshotManagerHandler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

    public async IAsyncEnumerable<IApplicableEvent<SnapshotManager>> HandleCommandAsync(Func<AggregateState<SnapshotManager>> getAggregateState, CreateSnapshotManager command)
    {
        await Task.CompletedTask;
        yield return new SnapshotManagerCreated(_sekibanDateProducer.UtcNow);
    }
}
