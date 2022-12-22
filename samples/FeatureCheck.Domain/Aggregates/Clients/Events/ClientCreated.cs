using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : IEventPayload<Client>
{
    public Client OnEvent(Client payload, IEvent ev) => new Client(BranchId, ClientName, ClientEmail);
}
