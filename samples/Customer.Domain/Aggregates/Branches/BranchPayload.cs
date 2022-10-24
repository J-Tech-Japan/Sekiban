using Sekiban.Core.Aggregate;
namespace Customer.Domain.Aggregates.Branches;

public record BranchPayload : IAggregatePayload
{
    public string Name { get; init; } = string.Empty;
}
