using FeatureCheck.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
using System.Collections.Immutable;

// ReSharper disable UnusedVariable
// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable NotAccessedPositionalProperty.Global
namespace FeatureCheck.Domain.Aggregates.Clients.Projections;

public record ClientNameHistoryProjection(
    Guid BranchId,
    ImmutableList<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord> ClientNames,
    string ClientEmail) : IDeletableSingleProjectionPayload<Client, ClientNameHistoryProjection>
{
    public bool IsDeleted { get; init; }

    public static ClientNameHistoryProjection? ApplyEvent<TEventPayload>(
        ClientNameHistoryProjection projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon
    {
        Func<ClientNameHistoryProjection>? func = ev.Payload switch
        {
            ClientCreated clientCreated => () => new ClientNameHistoryProjection(
                clientCreated.BranchId,
                new List<ClientNameHistoryProjectionRecord>
                {
                    new(clientCreated.ClientName, ev.TimeStamp)
                }.ToImmutableList(),
                clientCreated.ClientEmail),

            ClientNameChanged clientNameChanged => () =>
            {
                return projectionPayload with
                {
                    ClientNames = projectionPayload.ClientNames.Add(
                        new ClientNameHistoryProjectionRecord(clientNameChanged.ClientName, ev.TimeStamp))
                };
            },
            ClientDeleted => () => projectionPayload with
            {
                IsDeleted = true
            },
            _ => null
        };
        return func?.Invoke();
    }

    public static ClientNameHistoryProjection CreateInitialPayload() =>
        new(Guid.Empty, ImmutableList<ClientNameHistoryProjectionRecord>.Empty, string.Empty);

    public record ClientNameHistoryProjectionRecord(string Name, DateTime DateChanged);
}
