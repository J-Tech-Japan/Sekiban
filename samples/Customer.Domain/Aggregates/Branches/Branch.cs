using Customer.Domain.Aggregates.Branches.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Branches;

public class Branch : Aggregate<BranchPayload>
{
    public void Created(NameString name)
    {
        AddAndApplyEvent(new BranchCreated(name));
    }
    protected override Func<AggregateVariable<BranchPayload>, AggregateVariable<BranchPayload>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            BranchCreated branchCreated => _ => new AggregateVariable<BranchPayload>(new BranchPayload { Name = branchCreated.Name }),
            _ => null
        };
    }
}
