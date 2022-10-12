using Sekiban.EventSourcing.Shared;
namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;

public record CreateSnapshotManager : ICreateAggregateCommand<SnapshotManager>;
public class CreateSnapshotManagerHandler : CreateAggregateCommandHandlerBase<SnapshotManager, CreateSnapshotManager>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public CreateSnapshotManagerHandler(ISekibanDateProducer sekibanDateProducer)
    {
        _sekibanDateProducer = sekibanDateProducer;
    }
    protected override async Task ExecCreateCommandAsync(SnapshotManager aggregate, CreateSnapshotManager command)
    {
        await Task.CompletedTask;
        aggregate.Created(_sekibanDateProducer.UtcNow);
    }
    public override Guid GenerateAggregateId(CreateSnapshotManager command)
    {
        return SnapshotManager.SharedId;
    }
}
