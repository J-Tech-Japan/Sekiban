using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record SnapshotManager : ICommandBase<Aggregate.SnapshotManager>
{
    public Guid GetAggregateId() => Aggregate.SnapshotManager.SharedId;
}
public class CreateSnapshotManagerHandler : CreateCommandHandlerBase<Aggregate.SnapshotManager, SnapshotManager>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public CreateSnapshotManagerHandler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;
    protected override async IAsyncEnumerable<IApplicableEvent<Aggregate.SnapshotManager>> ExecCreateCommandAsync(
        Func<AggregateState<Aggregate.SnapshotManager>> getAggregateState,
        SnapshotManager command)
    {
        await Task.CompletedTask;
        yield return new SnapshotManagerCreated(_sekibanDateProducer.UtcNow);
    }
}
