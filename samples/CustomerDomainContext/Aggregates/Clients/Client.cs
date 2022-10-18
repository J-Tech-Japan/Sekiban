using CustomerDomainContext.Aggregates.Clients.Events;
namespace CustomerDomainContext.Aggregates.Clients;

public class Client : TransferableAggregateBase<ClientContents>
{
    public void CreateClient(Guid branchId, NameString clientName, EmailString clientEmail)
    {
        AddAndApplyEvent(new ClientCreated(branchId, clientName, clientEmail));
    }
    protected override Func<AggregateVariable<ClientContents>, AggregateVariable<ClientContents>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            ClientCreated clientChanged => _ => new AggregateVariable<ClientContents>(
                new ClientContents(clientChanged.BranchId, clientChanged.ClientName, clientChanged.ClientEmail)),
            ClientNameChanged clientNameChanged => variable =>
                variable with { Contents = Contents with { ClientName = clientNameChanged.ClientName } },
            ClientDeleted => variable => variable with { IsDeleted = true },
            _ => null
        };
    }
    public void ChangeClientName(NameString clientName)
    {
        var ev = new ClientNameChanged(clientName);
        // ValueObjectへの代入では行えない集約内の検証をここに記述する。
        AddAndApplyEvent(ev);
    }
    public void Delete()
    {
        AddAndApplyEvent(new ClientDeleted());
    }
}
