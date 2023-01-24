using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public class ClientDeleteCancelled : IEventPayload<Client, ClientDeleteCancelled>
{
    public static Client OnEvent(Client payload, Event<ClientDeleteCancelled> ev) => payload with { IsDeleted = false };
    public Client OnEventInstance(Client payload, Event<ClientDeleteCancelled> ev) => OnEvent(payload, ev);
}
