using Sekiban.Core.Event;

namespace Customer.Domain.Aggregates.Clients.Events;

public class ClientDeleteCancelled : IEventPayload<Client>
{
    public Client OnEvent(Client payload, IEvent ev)
    {
        return payload with { IsDeleted = false };
    }
}
