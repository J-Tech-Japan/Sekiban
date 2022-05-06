using CustomerDomainContext.Aggregates.Clients.Events;
// ReSharper disable UnusedVariable
// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable NotAccessedPositionalProperty.Global
namespace CustomerDomainContext.Aggregates.Clients.Projections;

/// <summary>
///     プロジェクションに関しては、高速化のために、データとDTOを共通かしている。
///     分割することも可能
/// </summary>
public class ClientNameHistoryProjection : SingleAggregateProjectionBase<ClientNameHistoryProjection>
{
    public Guid BranchId { get; set; } = Guid.Empty;
    public List<ClientNameHistoryProjectionRecord> ClientNames { get; init; } = new();
    public string ClientEmail { get; set; } = null!;
    public ClientNameHistoryProjection(Guid aggregateId) => AggregateId = aggregateId;

    public ClientNameHistoryProjection() { }

    public override ClientNameHistoryProjection ToDto() => this;
    public override void ApplySnapshot(ClientNameHistoryProjection snapshot)
    {
        BranchId = snapshot.BranchId;
        ClientNames.AddRange(snapshot.ClientNames);
        ClientEmail = snapshot.ClientEmail;
    }

    public override Type OriginalAggregateType() => typeof(Client);

    public override ClientNameHistoryProjection CreateInitialAggregate(Guid aggregateId) =>
        new(aggregateId);

    protected override Action GetApplyEventAction(AggregateEvent ev)
    {
        return ev switch
        {
            ClientCreated clientCreated => () =>
            {
                BranchId = clientCreated.BranchId;
                ClientNames.Add(new ClientNameHistoryProjectionRecord(clientCreated.ClientName, clientCreated.TimeStamp));
                ClientEmail = clientCreated.ClientEmail;
            },

            ClientNameChanged clientNameChanged => () =>
                ClientNames.Add(new ClientNameHistoryProjectionRecord(clientNameChanged.ClientName, clientNameChanged.TimeStamp)),

            ClientDeleted => () => IsDeleted = true,

            _ => throw new JJEventNotImplementedException()
        };
    }

    public record ClientNameHistoryProjectionRecord(string Name, DateTime DateChanged);
}
