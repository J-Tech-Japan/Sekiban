using Sekiban.Core.Events;
namespace MultiTenant.Domain.Aggregates.Clients.Events;

public record ClientCreated(string Name) : IEventPayload<ClientPayload, ClientCreated>
{

    public static ClientPayload OnEvent(ClientPayload aggregatePayload, Event<ClientCreated> ev) => new() { Name = ev.Payload.Name };
}
