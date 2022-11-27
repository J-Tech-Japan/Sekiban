using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientDeleted : IApplicableEvent<Client>
{
    public Client OnEvent(Client payload, IEvent ev) => payload with { IsDeleted = true };
}
