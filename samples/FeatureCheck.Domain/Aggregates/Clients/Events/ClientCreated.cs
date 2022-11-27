using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : IApplicableEvent<Client>
{
    public Client OnEvent(Client payload, IEvent ev) => new Client(BranchId, ClientName, ClientEmail);
}
