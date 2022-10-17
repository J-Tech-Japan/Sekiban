using CustomerDomainContext.Aggregates.Clients.Events;
using Sekiban.EventSourcing.Queries.SingleAggregates;
// ReSharper disable UnusedVariable
// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable NotAccessedPositionalProperty.Global
namespace CustomerDomainContext.Aggregates.Clients.Projections;

/// <summary>
///     プロジェクションに関しては、高速化のために、データとDTOを共通かしている。
///     分割することも可能
/// </summary>
public class ClientNameHistoryProjection : SingleAggregateProjectionBase<Client, ClientNameHistoryProjection,
    ClientNameHistoryProjection.ContentsDefinition>
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
    protected override Func<AggregateVariable<ContentsDefinition>, AggregateVariable<ContentsDefinition>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            ClientCreated clientCreated => _ =>
            {
                return new AggregateVariable<ContentsDefinition>(
                    new ContentsDefinition(
                        clientCreated.BranchId,
                        new List<ClientNameHistoryProjectionRecord> { new(clientCreated.ClientName, ev.TimeStamp) },
                        clientCreated.ClientEmail));
            },

            ClientNameChanged clientNameChanged => variable =>
            {
                var list = Contents.ClientNames.ToList();
                list.Add(new ClientNameHistoryProjectionRecord(clientNameChanged.ClientName, ev.TimeStamp));
                return variable with { Contents = Contents with { ClientNames = list } };
            },
            ClientDeleted => variable => variable with { IsDeleted = true },
            _ => null
        };
    }
    // protected override Action? GetApplyEventAction(IAggregateEvent ev)
    // {
    //     return ev.GetPayload() switch
    //     {
    //         ClientCreated clientCreated => () =>
    //         {
    //             Contents = new ContentsDefinition(
    //                 clientCreated.BranchId,
    //                 new List<ClientNameHistoryProjectionRecord> { new(clientCreated.ClientName, ev.TimeStamp) },
    //                 clientCreated.ClientEmail);
    //         },
    //
    //         ClientNameChanged clientNameChanged => () =>
    //         {
    //             var list = Contents.ClientNames.ToList();
    //             list.Add(new ClientNameHistoryProjectionRecord(clientNameChanged.ClientName, ev.TimeStamp));
    //             Contents = Contents with { ClientNames = list };
    //         },
    //         ClientDeleted => () => IsDeleted = true,
    //         _ => null
    //     };
    // }
    public record ContentsDefinition(
        Guid BranchId,
        IReadOnlyCollection<ClientNameHistoryProjectionRecord> ClientNames,
        string ClientEmail) : ISingleAggregateProjectionContents;

    public record ClientNameHistoryProjectionRecord(string Name, DateTime DateChanged);
}
