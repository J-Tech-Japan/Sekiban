using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public class ClientDeleteCancelled : IChangedEvent<Client>
{
    public Client OnEvent(Client payload, IEvent ev) => payload with { IsDeleted = false };
}
