using Orleans;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record CreateClient(Guid BranchId, string Name, string Email) 
    : ICommandWithHandler<CreateClient, ClientProjector>
{
    public PartitionKeys SpecifyPartitionKeys(CreateClient command) => 
        PartitionKeys<ClientProjector>.Generate();
        
    public ResultBox<EventOrNone> Handle(CreateClient command, ICommandContext<IAggregatePayload> context) =>
        EventOrNone.Event(new ClientCreated(command.BranchId, command.Name, command.Email));
}
