using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientDeleted : IEventPayload<Client>
{
    public Client OnEvent(Client payload, IEvent ev) => payload with { IsDeleted = true };
}
