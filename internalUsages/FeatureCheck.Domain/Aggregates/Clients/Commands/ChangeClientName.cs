using FeatureCheck.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record ChangeClientName(Guid ClientId, string ClientName)
    : ICommandWithVersionValidation<Client>, ICleanupNecessaryCommand<ChangeClientName>
{
    public ChangeClientName() : this(Guid.Empty, string.Empty)
    {
    }

    public ChangeClientName CleanupCommand(ChangeClientName command) => command with
    {
        ClientName = "stripped for security"
    };

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;

    public class Handler : ICommandHandler<Client, ChangeClientName>
    {
        public IEnumerable<IEventPayloadApplicableTo<Client>> HandleCommand(
            ChangeClientName command,
            ICommandContext<Client> context)
        {
            yield return new ClientNameChanged(command.ClientName);
        }
        public Guid SpecifyAggregateId(ChangeClientName command) => command.ClientId;
    }
}
