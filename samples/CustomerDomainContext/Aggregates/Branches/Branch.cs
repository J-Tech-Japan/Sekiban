using CustomerDomainContext.Aggregates.Branches.Events;
namespace CustomerDomainContext.Aggregates.Branches;

public class Branch : TransferableAggregateBase<BranchContents>
{
    public void Created(NameString name)
    {
        AddAndApplyEvent(new BranchCreated(name));
    }
    protected override Func<AggregateVariable<BranchContents>, AggregateVariable<BranchContents>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            BranchCreated branchCreated => _ => new AggregateVariable<BranchContents>(new BranchContents { Name = branchCreated.Name }),
            _ => null
        };
    }
}
