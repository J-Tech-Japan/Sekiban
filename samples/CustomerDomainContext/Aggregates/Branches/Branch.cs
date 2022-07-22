using CustomerDomainContext.Aggregates.Branches.Events;
namespace CustomerDomainContext.Aggregates.Branches;

public class Branch : TransferableAggregateBase<BranchContents>
{
    public Branch(Guid branchId) : base(branchId) { }

    public Branch(NameString name) : base(Guid.NewGuid())
    {
        AddAndApplyEvent(new BranchCreated(name));
    }

    protected override Action? GetApplyEventAction(IAggregateEvent ev) =>
        ev.Payload switch
        {
            BranchCreated branchCreated => () =>
            {
                Contents = new BranchContents { Name = branchCreated.Name };
            },
            _ => null
        };
}
