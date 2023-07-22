using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public class CreateSnapshotManagerHandler : ICommandHandler<SnapshotManager, CreateSnapshotManager>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;

    public CreateSnapshotManagerHandler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

    public async IAsyncEnumerable<IEventPayloadApplicableTo<SnapshotManager>> HandleCommandAsync(
        Func<AggregateState<SnapshotManager>> getAggregateState,
        CreateSnapshotManager command)
    {
        await Task.CompletedTask;
        yield return new SnapshotManagerCreated(_sekibanDateProducer.UtcNow);
    }
}
