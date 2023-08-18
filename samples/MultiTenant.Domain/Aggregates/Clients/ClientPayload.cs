using Sekiban.Core.Aggregate;
namespace MultiTenant.Domain.Aggregates.Clients;

public class ClientPayload : IAggregatePayload<ClientPayload>
{
    public string Name { get; init; } = string.Empty;
    public static ClientPayload CreateInitialPayload(ClientPayload? _) => new();
}
