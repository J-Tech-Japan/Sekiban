using CustomerDomainContext.Aggregates.Branches.Events;
namespace CustomerDomainContext.Aggregates.Branches;

public class Branch : TransferableAggregateBase<BranchDto>
{
    private NameString Name { get; set; } = null!;

    public Branch(Guid branchId) : base(branchId) { }

    public Branch(NameString name) : base(Guid.NewGuid())
    {
        AddAndApplyEvent(new BranchCreated(AggregateId, name));
    }

    public override BranchDto ToDto() => new(this)
    {
        Name = Name
    };

    protected override void CopyPropertiesFromSnapshot(BranchDto snapshot)
    {
        Name = snapshot.Name;
    }

    protected override Action? GetApplyEventAction(AggregateEvent ev) => ev switch
    {
        BranchCreated branchCreated => () =>
        {
            Name = branchCreated.Name;
        },
        _ => null
    };
}
