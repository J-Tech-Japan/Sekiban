using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : IEventPayload<Client, ClientCreated>
{
    public static Client OnEvent(Client payload, Event<ClientCreated> ev) => new(ev.Payload.BranchId, ev.Payload.ClientName, ev.Payload.ClientEmail);
    public Client OnEventInstance(Client payload, Event<ClientCreated> ev) => OnEvent(payload, ev);
}
