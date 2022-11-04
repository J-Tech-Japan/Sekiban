using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleProjections;
// ReSharper disable UnusedVariable
// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable NotAccessedPositionalProperty.Global
namespace Customer.Domain.Aggregates.Clients.Projections;

public class ClientNameHistorySingleProjection : SingleProjectionBase<Client, ClientNameHistorySingleProjection,
    ClientNameHistorySingleProjection.PayloadDefinition>
{
    protected override Func<PayloadDefinition, PayloadDefinition>? GetApplyEventFunc(
        IEvent ev,
        IEventPayload eventPayload)
    {
        return eventPayload switch
        {
            ClientCreated clientCreated => _ =>
                new PayloadDefinition(
                    clientCreated.BranchId,
                    new List<ClientNameHistoryProjectionRecord> { new(clientCreated.ClientName, ev.TimeStamp) },
                    clientCreated.ClientEmail),

            ClientNameChanged clientNameChanged => p =>
            {
                var list = Payload.ClientNames.ToList();
                list.Add(new ClientNameHistoryProjectionRecord(clientNameChanged.ClientName, ev.TimeStamp));
                return p with { ClientNames = list };
            },
            ClientDeleted => p => p with { IsDeleted = true },
            _ => null
        };
    }
    public record PayloadDefinition(
        Guid BranchId,
        IReadOnlyCollection<ClientNameHistoryProjectionRecord> ClientNames,
        string ClientEmail,
        bool IsDeleted = false) : IDeletableSingleProjectionPayload;

    public record ClientNameHistoryProjectionRecord(string Name, DateTime DateChanged);
}
