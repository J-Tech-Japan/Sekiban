namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands
{
    public record CreateSnapshotManager : ICreateAggregateCommand<SnapshotManager>;
    public class CreateSnapshotManagerHandler : CreateAggregateCommandHandlerBase<SnapshotManager, CreateSnapshotManager>
    {
        protected override async Task ExecCreateCommandAsync(SnapshotManager aggregate, CreateSnapshotManager command)
        {
            await Task.CompletedTask;
            aggregate.Created(DateTime.UtcNow);
        }
        public override Guid GenerateAggregateId(CreateSnapshotManager command) =>
            SnapshotManager.SharedId;
    }
}
