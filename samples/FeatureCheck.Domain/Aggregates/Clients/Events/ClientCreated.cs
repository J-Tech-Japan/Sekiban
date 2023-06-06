using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : IEventPayload<Client, ClientCreated>
{
    public Client OnEventInstance(Client aggregatePayload, Event<ClientCreated> ev) => OnEvent(aggregatePayload, ev);
    public static Client OnEvent(Client aggregatePayload, Event<ClientCreated> ev) =>
        new(ev.Payload.BranchId, ev.Payload.ClientName, ev.Payload.ClientEmail);
}
