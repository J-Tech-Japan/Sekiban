using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : ICreatedEvent<Client>
{
    public Client OnEvent(Client payload, IEvent ev) => new Client(BranchId, ClientName, ClientEmail);
}
