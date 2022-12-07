using Sekiban.Core.Event;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : IEventPayload<Client>
{
    public Client OnEvent(Client payload, IEvent ev)
    {
        return new(BranchId, ClientName, ClientEmail);
    }
}
