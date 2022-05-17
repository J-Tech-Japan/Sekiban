using CustomerDomainContext.Aggregates.Clients.Events;
namespace CustomerDomainContext.Aggregates.Clients;

public class Client : TransferableAggregateBase<ClientDto>
{
    private Guid BranchId { get; set; }
    private NameString ClientName { get; set; } = null!;
    private EmailString ClientEmail { get; set; } = null!;

    public Client(Guid clientId) : base(clientId) { }

    public Client(Guid branchId, NameString clientName, EmailString clientEmail) : base(Guid.NewGuid())
    {
        AddAndApplyEvent(new ClientCreated(AggregateId, branchId, clientName, clientEmail));
    }

    public override ClientDto ToDto() =>
        new(this) { BranchId = BranchId, ClientName = ClientName, ClientEmail = ClientEmail };

    protected override void CopyPropertiesFromSnapshot(ClientDto snapshot)
    {
        BranchId = snapshot.BranchId;
        ClientName = snapshot.ClientName;
        ClientEmail = snapshot.ClientEmail;
    }

    protected override Action? GetApplyEventAction(AggregateEvent ev) =>
        ev switch
        {
            ClientCreated clientChanged => () =>
            {
                BranchId = clientChanged.BranchId;
                ClientName = clientChanged.ClientName;
                ClientEmail = clientChanged.ClientEmail;
            },

            ClientNameChanged clientNameChanged => () => ClientName = clientNameChanged.ClientName,

            ClientDeleted => () => IsDeleted = true,

            _ => null
        };

    public void ChangeClientName(NameString clientName)
    {
        var ev = new ClientNameChanged(AggregateId, clientName);
        // ValueObjectへの代入では行えない集約内の検証をここに記述する。
        AddAndApplyEvent(ev);
    }

    public void Delete() =>
        AddAndApplyEvent(new ClientDeleted(AggregateId));
}
