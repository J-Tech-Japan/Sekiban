using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientNameChanged(string ClientName) : IChangedEvent<Client>
{
    public Client OnEvent(Client payload, IAggregateEvent aggregateEvent)
    {
        return payload with { ClientName = ClientName };
    }
}
