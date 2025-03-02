using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace AspireEventSample.ApiService.Aggregates.Branches;

[GenerateSerializer]
public record ChangeBranchName(Guid BranchId, string NameToChange)
    : ICommandWithHandler<ChangeBranchName, BranchProjector>
{
    public ResultBox<EventOrNone> Handle(ChangeBranchName command, ICommandContext<IAggregatePayload> context) =>
        context.AppendEvent(new BranchNameChanged(command.NameToChange));
    public PartitionKeys SpecifyPartitionKeys(ChangeBranchName command) =>
        PartitionKeys<BranchProjector>.Existing(BranchId);
}