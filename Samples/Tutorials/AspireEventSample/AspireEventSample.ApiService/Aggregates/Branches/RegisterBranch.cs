using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using System.ComponentModel.DataAnnotations;

namespace AspireEventSample.ApiService.Aggregates.Branches;

[GenerateSerializer]
public record RegisterBranch([property:Required]string Name, [property:Required]string Country) : ICommandWithHandler<RegisterBranch, BranchProjector>
{
    public PartitionKeys SpecifyPartitionKeys(RegisterBranch command) => PartitionKeys<BranchProjector>.Generate();
    public ResultBox<EventOrNone> Handle(RegisterBranch command, ICommandContext<IAggregatePayload> context) =>
        EventOrNone.Event(new BranchCreated(command.Name, command.Country));
}
