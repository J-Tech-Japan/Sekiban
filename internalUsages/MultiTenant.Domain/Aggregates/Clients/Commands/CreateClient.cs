using MultiTenant.Domain.Aggregates.Clients.Events;
using ResultBoxes;
using Sekiban.Core.Command;
using System.ComponentModel.DataAnnotations;
namespace MultiTenant.Domain.Aggregates.Clients.Commands;

public class CreateClient : ITenantCommandWithHandler<ClientPayload, CreateClient>
{
    [Required]
    [MaxLength(30)]
    public string Name { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public static Guid SpecifyAggregateId(CreateClient command) => Guid.NewGuid();
    public static ResultBox<UnitValue> HandleCommand(CreateClient command, ICommandContext<ClientPayload> context) =>
        context.AppendEvent(new ClientCreated(command.Name));
    public static string GetTenantId(CreateClient command) => command.TenantId;
}
