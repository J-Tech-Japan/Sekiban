using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public class ClientDeleteCancelled : IEventPayload<Client, ClientDeleteCancelled>
{
    public static Client OnEvent(Client aggregatePayload, Event<ClientDeleteCancelled> ev) => aggregatePayload with { IsDeleted = false };
    public Client OnEventInstance(Client aggregatePayload, Event<ClientDeleteCancelled> ev) => OnEvent(aggregatePayload, ev);
}
