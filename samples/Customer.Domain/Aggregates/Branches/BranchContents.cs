using Sekiban.Core.Aggregate;
namespace Customer.Domain.Aggregates.Branches;

public record BranchContents : IAggregateContents
{
    public string Name { get; init; } = string.Empty;
}
