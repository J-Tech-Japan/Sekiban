using Sekiban.Core.Aggregate;
namespace MultiTenant.Domain.Aggregates.Clients;

public class ClientPayload : IAggregatePayload
{
    public string Name { get; init; } = string.Empty;
    public static IAggregatePayloadCommon CreateInitialPayload() => new ClientPayload();
}
