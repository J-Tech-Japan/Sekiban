using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record CreateSnapshotManager : ICreateCommand<SnapshotManager>
{
    public Guid GetAggregateId() => SnapshotManager.SharedId;
}
public class CreateSnapshotManagerHandler : CreateCommandHandlerBase<SnapshotManager, CreateSnapshotManager>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public CreateSnapshotManagerHandler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;
    protected override async IAsyncEnumerable<IApplicableEvent<SnapshotManager>> ExecCreateCommandAsync(
        Func<AggregateIdentifierState<SnapshotManager>> getAggregateState,
        CreateSnapshotManager command)
    {
        await Task.CompletedTask;
        yield return new SnapshotManagerCreated(_sekibanDateProducer.UtcNow);
    }
}
