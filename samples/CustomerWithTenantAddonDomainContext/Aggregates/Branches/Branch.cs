using CustomerWithTenantAddonDomainContext.Aggregates.Branches.Events;
namespace CustomerWithTenantAddonDomainContext.Aggregates.Branches;

public class Branch : TransferableAggregateBase<BranchContents>
{
    public void Created(NameString name)
    {
        AddAndApplyEvent(new BranchCreated(name));
    }

    protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload)
    {
        return payload switch
        {
            BranchCreated branchCreated => () =>
            {
                Contents = new BranchContents { Name = branchCreated.Name };
            },
            _ => null
        };
    }
}
