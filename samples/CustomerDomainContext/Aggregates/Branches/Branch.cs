using CustomerDomainContext.Aggregates.Branches.Events;
namespace CustomerDomainContext.Aggregates.Branches
{
    public class Branch : TransferableAggregateBase<BranchContents>
    {
        public void Created(NameString name)
        {
            AddAndApplyEvent(new BranchCreated(name));
        }

        protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload) =>
            payload switch
            {
                BranchCreated branchCreated => () =>
                {
                    Contents = new BranchContents { Name = branchCreated.Name };
                },
                _ => null
            };
    }
}
