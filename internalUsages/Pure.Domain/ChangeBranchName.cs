using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
namespace Pure.Domain;

public record ChangeBranchName(Guid BranchId, string NameToChange)
    : ICommandWithHandler<ChangeBranchName, BranchProjector>
{
    public ResultBox<EventOrNone> Handle(ChangeBranchName command, ICommandContext<IAggregatePayload> context) =>
        context.AppendEvent(new BranchNameChanged(command.NameToChange));
    public PartitionKeys SpecifyPartitionKeys(ChangeBranchName command) =>
        PartitionKeys<BranchProjector>.Existing(BranchId);
}
