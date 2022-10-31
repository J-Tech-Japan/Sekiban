using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientNameChanged(string ClientName) : IChangedEvent<Client>
{
    public Client OnEvent(Client payload, IEvent ev) => payload with { ClientName = ClientName };
}
