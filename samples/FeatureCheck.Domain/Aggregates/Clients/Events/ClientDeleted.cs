using Sekiban.Core.Event;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientDeleted : IEventPayload<Client>
{
    public Client OnEvent(Client payload, IEvent ev)
    {
        return payload with { IsDeleted = true };
    }
}
