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
    public Func<ClientNameHistoryProjection>? GetApplyEventFuncInstance<TEventPayload>(
        ClientNameHistoryProjection projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon =>
        GetApplyEventFunc(projectionPayload, ev);

    public static Func<ClientNameHistoryProjection>? GetApplyEventFunc<TEventPayload>(
        ClientNameHistoryProjection projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon
    {
        return ev.Payload switch
        {
            ClientCreated clientCreated => () =>
                new ClientNameHistoryProjection(
                    clientCreated.BranchId,
                    new List<ClientNameHistoryProjectionRecord> { new(clientCreated.ClientName, ev.TimeStamp) },
                    clientCreated.ClientEmail),

            ClientNameChanged clientNameChanged => () =>
            {
                var list = projectionPayload.ClientNames.ToList();
                list.Add(new ClientNameHistoryProjectionRecord(clientNameChanged.ClientName, ev.TimeStamp));
                return projectionPayload with { ClientNames = list };
            },
            ClientDeleted => () => projectionPayload with { IsDeleted = true },
            _ => null
        };
    }

    public record ClientNameHistoryProjectionRecord(string Name, DateTime DateChanged);
}
