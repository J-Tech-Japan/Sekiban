namespace CustomerDomainContext.Aggregates.Branches;

public record BranchDto : AggregateDtoBase
{
    public string Name { get; init; } = null!;

    public BranchDto() { }

    public BranchDto(Branch aggregate) : base(aggregate) { }
}
