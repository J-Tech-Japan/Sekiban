using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record CreateSnapshotManager : ICreateAggregateCommand<SnapshotManager>
{
    public Guid GetAggregateId()
    {
        return SnapshotManager.SharedId;
    }
}
public class CreateSnapshotManagerHandler : CreateAggregateCommandHandlerBase<SnapshotManager, CreateSnapshotManager>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public CreateSnapshotManagerHandler(ISekibanDateProducer sekibanDateProducer)
    {
        _sekibanDateProducer = sekibanDateProducer;
    }
    protected override async IAsyncEnumerable<IApplicableEvent<SnapshotManager>> ExecCreateCommandAsync(
        AggregateState<SnapshotManager> aggregateState,
        CreateSnapshotManager command)
    {
        await Task.CompletedTask;
        yield return new SnapshotManagerCreated(_sekibanDateProducer.UtcNow);
    }
}
