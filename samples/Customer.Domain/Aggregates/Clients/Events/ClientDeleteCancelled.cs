using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public class ClientDeleteCancelled : IChangedEvent<Client>
{
    public Client OnEvent(Client payload, IAggregateEvent aggregateEvent)
    {
        return payload with { IsDeleted = false };
    }
}
