using CustomerWithTenantAddonDomainContext.Aggregates.Clients.Events;
namespace CustomerWithTenantAddonDomainContext.Aggregates.Clients;

public class Client : TransferableAggregateBase<ClientContents>
{
    public void CreateClient(Guid branchId, NameString clientName, EmailString clientEmail)
    {
        AddAndApplyEvent(new ClientCreated(branchId, clientName, clientEmail));
    }

    // protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload)
    // {
    //     return payload switch
    //     {
    //         ClientCreated clientChanged => () =>
    //         {
    //             Contents = new ClientContents(clientChanged.BranchId, clientChanged.ClientName, clientChanged.ClientEmail);
    //         },
    //
    //         ClientNameChanged clientNameChanged => () => Contents = Contents with { ClientName = clientNameChanged.ClientName },
    //
    //         ClientDeleted => () => IsDeleted = true,
    //
    //         _ => null
    //     };
    // }
    protected override Func<AggregateVariable<ClientContents>, AggregateVariable<ClientContents>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            ClientCreated clientCreated => _ =>
                new AggregateVariable<ClientContents>(
                    new ClientContents(clientCreated.BranchId, clientCreated.ClientName, clientCreated.ClientEmail)),
            ClientNameChanged clientNameChanged => variable =>
                variable with { Contents = variable.Contents with { ClientName = clientNameChanged.ClientName } },
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
