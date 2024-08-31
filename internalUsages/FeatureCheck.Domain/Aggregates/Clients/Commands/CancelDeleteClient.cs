using FeatureCheck.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record CancelDeleteClient : ICommandWithVersionValidation<Client>, ICancelDeletedCommand
{
    public Guid ClientId { get; init; }

    [Required]
    public string Reason { get; init; } = string.Empty;

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;

    public class Handler : ICommandHandler<Client, CancelDeleteClient>
    {
        public IEnumerable<IEventPayloadApplicableTo<Client>> HandleCommand(
            CancelDeleteClient command,
            ICommandContext<Client> context)
        {
            yield return new ClientDeleteCancelled();
        }
        public Guid SpecifyAggregateId(CancelDeleteClient command) => command.ClientId;
    }
}
