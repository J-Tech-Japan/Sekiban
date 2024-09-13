using Sekiban.Core.Aggregate;
namespace MultiTenant.Domain.Aggregates.Clients;

public record ClientPayload : ITenantAggregatePayload<ClientPayload>
{
    public string Name { get; init; } = string.Empty;
    public static ClientPayload CreateInitialPayload(ClientPayload? _) => new();
}
