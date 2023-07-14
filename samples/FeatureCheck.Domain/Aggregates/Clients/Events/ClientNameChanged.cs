using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientNameChanged(string ClientName) : IEventPayload<Client, ClientNameChanged>
{
    public static Client OnEvent(Client aggregatePayload, Event<ClientNameChanged> ev) =>
        aggregatePayload with { ClientName = ev.Payload.ClientName };
}
