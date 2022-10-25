using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleAggregate;
// ReSharper disable UnusedVariable
// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable NotAccessedPositionalProperty.Global
namespace Customer.Domain.Aggregates.Clients.Projections;

/// <summary>
///     プロジェクションに関しては、高速化のために、データとDTOを共通かしている。
///     分割することも可能
/// </summary>
public class ClientNameHistoryProjection : SingleAggregateProjectionBase<Client, ClientNameHistoryProjection,
    ClientNameHistoryProjection.PayloadDefinition>
{
    public ClientNameHistoryProjection(Guid aggregateId)
    {
        AggregateId = aggregateId;
    }
    public ClientNameHistoryProjection() { }
    public override ClientNameHistoryProjection CreateInitialAggregate(Guid aggregateId)
    {
        return new ClientNameHistoryProjection(aggregateId);
    }
    protected override Func<PayloadDefinition, PayloadDefinition>? GetApplyEventFunc(
        IAggregateEvent ev,
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
        bool IsDeleted = false) : IDeletableSingleAggregateProjectionPayload;

    public record ClientNameHistoryProjectionRecord(string Name, DateTime DateChanged);
}
