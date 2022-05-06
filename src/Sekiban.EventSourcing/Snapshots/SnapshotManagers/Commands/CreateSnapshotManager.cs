namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;

public record CreateSnapshotManager(Guid AggregateId) : ICreateAggregateCommand<SnapshotManager>;
public class CreateSnapshotManagerHandler : CreateAggregateCommandHandlerBase<SnapshotManager, CreateSnapshotManager>
{
    protected override async Task<SnapshotManager> CreateAggregateAsync(CreateSnapshotManager command)
    {
        await Task.CompletedTask;
        return new SnapshotManager(command.AggregateId, DateTime.UtcNow);
    }
}
