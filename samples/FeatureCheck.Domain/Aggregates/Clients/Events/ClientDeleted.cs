using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientDeleted : IEventPayload<Client, ClientDeleted>
{
    public static Client OnEvent(Client aggregatePayload, Event<ClientDeleted> ev) => aggregatePayload with { IsDeleted = true };
    public Client OnEventInstance(Client aggregatePayload, Event<ClientDeleted> ev) => OnEvent(aggregatePayload, ev);
}
