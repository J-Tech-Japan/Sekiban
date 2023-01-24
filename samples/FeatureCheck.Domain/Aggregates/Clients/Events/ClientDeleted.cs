using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientDeleted : IEventPayload<Client, ClientDeleted>
{
    public static Client OnEvent(Client payload, Event<ClientDeleted> ev) => payload with { IsDeleted = true };
    public Client OnEventInstance(Client payload, Event<ClientDeleted> ev) => OnEvent(payload, ev);
}
