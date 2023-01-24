using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientNameChanged(string ClientName) : IEventPayload<Client, ClientNameChanged>
{
    public Client OnEventInstance(Client payload, Event<ClientNameChanged> ev) => OnEvent(payload, ev);

    public static Client OnEvent(Client payload, Event<ClientNameChanged> ev) => payload with { ClientName = ev.Payload.ClientName };
}
