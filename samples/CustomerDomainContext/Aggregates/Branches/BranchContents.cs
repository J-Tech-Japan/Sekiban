namespace CustomerDomainContext.Aggregates.Branches;

public record BranchContents : IAggregateContents
{
    public string Name { get; init; } = string.Empty;
}
