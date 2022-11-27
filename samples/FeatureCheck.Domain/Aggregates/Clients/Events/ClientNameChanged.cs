using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientNameChanged(string ClientName) : IApplicableEvent<Client>
{
    public Client OnEvent(Client payload, IEvent ev) => payload with { ClientName = ClientName };
}
