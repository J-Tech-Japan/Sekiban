using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record CreateSnapshotManager : ICreateAggregateCommand<SnapshotManagerPayload>;
public class CreateSnapshotManagerHandler : CreateAggregateCommandHandlerBase<SnapshotManagerPayload, CreateSnapshotManager>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public CreateSnapshotManagerHandler(ISekibanDateProducer sekibanDateProducer)
    {
        _sekibanDateProducer = sekibanDateProducer;
    }
    public override Guid GenerateAggregateId(CreateSnapshotManager command)
    {
        return SnapshotManagerPayload.SharedId;
    }
    protected override async IAsyncEnumerable<IApplicableEvent<SnapshotManagerPayload>> ExecCreateCommandAsync(
        AggregateState<SnapshotManagerPayload> aggregate,
        CreateSnapshotManager command)
    {
        await Task.CompletedTask;
        yield return new SnapshotManagerCreated(_sekibanDateProducer.UtcNow);
    }
}
