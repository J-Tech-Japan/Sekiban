using MultiTenant.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace MultiTenant.Domain.Aggregates.Clients.Commands;

public class CreateClient : ITenantCommand<ClientPayload>
{
    [Required]
    [MaxLength(30)]
    public string Name { get; init; } = string.Empty;
    public Guid GetAggregateId() => Guid.NewGuid();
    public string TenantId { get; init; } = string.Empty;

    public class Handler : ICommandHandler<ClientPayload, CreateClient>
    {
        public IEnumerable<IEventPayloadApplicableTo<ClientPayload>> HandleCommand(CreateClient command, ICommandContext<ClientPayload> context)
        {
            yield return new ClientCreated(command.Name);
        }
    }
}
