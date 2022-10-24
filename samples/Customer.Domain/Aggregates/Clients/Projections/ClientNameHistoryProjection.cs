using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
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
    protected override Func<AggregateVariable<PayloadDefinition>, AggregateVariable<PayloadDefinition>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            ClientCreated clientCreated => _ => new AggregateVariable<PayloadDefinition>(
                new PayloadDefinition(
                    clientCreated.BranchId,
                    new List<ClientNameHistoryProjectionRecord> { new(clientCreated.ClientName, ev.TimeStamp) },
                    clientCreated.ClientEmail)),

            ClientNameChanged clientNameChanged => variable =>
            {
                var list = Payload.ClientNames.ToList();
                list.Add(new ClientNameHistoryProjectionRecord(clientNameChanged.ClientName, ev.TimeStamp));
                return variable with { Contents = Payload with { ClientNames = list } };
            },
            ClientDeleted => variable => variable with { IsDeleted = true },
            _ => null
        };
    }
    public record PayloadDefinition(
        Guid BranchId,
        IReadOnlyCollection<ClientNameHistoryProjectionRecord> ClientNames,
        string ClientEmail) : ISingleAggregateProjectionPayload;

    public record ClientNameHistoryProjectionRecord(string Name, DateTime DateChanged);
}
