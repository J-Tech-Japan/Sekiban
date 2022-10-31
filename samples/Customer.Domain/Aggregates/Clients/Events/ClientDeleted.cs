using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientDeleted : IChangedEvent<Client>
{
    public Client OnEvent(Client payload, IEvent @event) => payload with { IsDeleted = true };
}
