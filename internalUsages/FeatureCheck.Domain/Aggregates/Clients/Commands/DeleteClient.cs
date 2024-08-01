using FeatureCheck.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record DeleteClient(Guid ClientId) : ICommandWithVersionValidation<Client>
{
    public DeleteClient() : this(Guid.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;

    public class Handler : ICommandHandler<Client, DeleteClient>
    {
        public IEnumerable<IEventPayloadApplicableTo<Client>> HandleCommand(DeleteClient command,
            ICommandContext<Client> context)
        {
            yield return new ClientDeleted();
        }
    }
}
