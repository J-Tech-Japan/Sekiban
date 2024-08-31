using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record ClientNoEventsCommand : ICommand<Client>
{
    public Guid ClientId { get; init; }

    public class Handler : ICommandHandler<Client, ClientNoEventsCommand>
    {
        public IEnumerable<IEventPayloadApplicableTo<Client>> HandleCommand(
            ClientNoEventsCommand command,
            ICommandContext<Client> context)
        {
            yield break;
        }
        public Guid SpecifyAggregateId(ClientNoEventsCommand command) => command.ClientId;
    }
}
