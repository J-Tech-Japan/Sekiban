using Sekiban.Core.Events;
namespace MultiTenant.Domain.Aggregates.Clients.Events;

public record ClientCreated(string Name) : IEventPayload<ClientPayload, ClientCreated>
{
    // for dotnet 6
    public ClientPayload OnEventInstance(ClientPayload aggregatePayload, Event<ClientCreated> ev) => OnEvent(aggregatePayload, ev);

    public static ClientPayload OnEvent(ClientPayload aggregatePayload, Event<ClientCreated> ev) => new() { Name = ev.Payload.Name };
}
