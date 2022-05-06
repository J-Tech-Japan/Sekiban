namespace CustomerDomainContext.Aggregates.Branches.Events;

public record BranchCreated(Guid BranchId, string Name) : CreateAggregateEvent<Branch>(BranchId);
