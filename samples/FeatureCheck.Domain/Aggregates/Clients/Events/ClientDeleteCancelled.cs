using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public class ClientDeleteCancelled : IEventPayload<Client>
{
    public Client OnEvent(Client payload, IEvent ev)
    {
        return payload with { IsDeleted = false };
    }
}
