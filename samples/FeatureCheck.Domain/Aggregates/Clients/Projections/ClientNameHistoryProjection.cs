using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleProjections;
// ReSharper disable UnusedVariable
// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable NotAccessedPositionalProperty.Global
namespace Customer.Domain.Aggregates.Clients.Projections;

public record ClientNameHistoryProjection(
    Guid BranchId,
    IReadOnlyCollection<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord> ClientNames,
    string ClientEmail) : DeletableSingleProjectionPayloadBase<Client, ClientNameHistoryProjection>()
{
    public ClientNameHistoryProjection() : this(Guid.Empty, new List<ClientNameHistoryProjectionRecord>(), string.Empty) { }
    public override Func<ClientNameHistoryProjection, ClientNameHistoryProjection>? GetApplyEventFunc(
        IEvent ev,
        IEventPayload eventPayload)
    {
        return eventPayload switch
        {
            ClientCreated clientCreated => _ =>
                new ClientNameHistoryProjection(
                    clientCreated.BranchId,
                    new List<ClientNameHistoryProjectionRecord> { new(clientCreated.ClientName, ev.TimeStamp) },
                    clientCreated.ClientEmail),

            ClientNameChanged clientNameChanged => p =>
            {
                var list = p.ClientNames.ToList();
                list.Add(new ClientNameHistoryProjectionRecord(clientNameChanged.ClientName, ev.TimeStamp));
                return p with { ClientNames = list };
            },
            ClientDeleted => p => p with { IsDeleted = true },
            _ => null
        };
    }
    public record ClientNameHistoryProjectionRecord(string Name, DateTime DateChanged);
}
