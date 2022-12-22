using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientNameChanged(string ClientName) : IEventPayload<Client>
{
    public Client OnEvent(Client payload, IEvent ev)
    {
        return payload with { ClientName = ClientName };
    }
}
