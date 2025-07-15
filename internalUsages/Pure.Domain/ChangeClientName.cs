using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer(GenerateFieldIds = GenerateFieldIds.PublicProperties)]
public record ChangeClientName(Guid ClientId, string Name) : ICommandWithHandler<ChangeClientName, ClientProjector>
{
    public int? ReferenceVersion { get; init; }

    public PartitionKeys SpecifyPartitionKeys(ChangeClientName command) =>
        PartitionKeys<ClientProjector>.Existing(ClientId);

    public ResultBox<EventOrNone> Handle(ChangeClientName command, ICommandContext<IAggregatePayload> context) =>
        EventOrNone.Event(new ClientNameChanged(command.Name));
}
