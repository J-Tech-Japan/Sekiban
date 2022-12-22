using FeatureCheck.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;

// ReSharper disable UnusedVariable
// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable NotAccessedPositionalProperty.Global
namespace FeatureCheck.Domain.Aggregates.Clients.Projections;

public record ClientNameHistoryProjection(
    Guid BranchId,
    IReadOnlyCollection<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord> ClientNames,
    string ClientEmail) : IDeletableSingleProjectionPayload<Client, ClientNameHistoryProjection>
{
    public ClientNameHistoryProjection() : this(Guid.Empty, new List<ClientNameHistoryProjectionRecord>(), string.Empty)
    {
    }
    public bool IsDeleted { get; init; }

    public Func<ClientNameHistoryProjection, ClientNameHistoryProjection>? GetApplyEventFunc(
        IEvent ev,
        IEventPayloadCommon eventPayload)
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
