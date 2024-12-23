using ResultBoxes;
using Sekiban.Core.Command;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate.Commands;

public record CreateSnapshotManagerAsync : ICommandWithHandlerAsync<SnapshotManager, CreateSnapshotManagerAsync>
{
    public static Task<ResultBox<EventOrNone<SnapshotManager>>> HandleCommandAsync(
        CreateSnapshotManagerAsync command,
        ICommandContext<SnapshotManager> context) =>
        context
            .GetRequiredService<ISekibanDateProducer>()
            .Conveyor(
                sekibanDateProducer => context.AppendEvent(new SnapshotManagerCreated(sekibanDateProducer.UtcNow)))
            .ToTask();
    public static Guid SpecifyAggregateId(CreateSnapshotManagerAsync command) => SnapshotManager.SharedId;
}
