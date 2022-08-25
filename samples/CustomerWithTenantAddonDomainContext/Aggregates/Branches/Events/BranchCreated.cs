namespace CustomerWithTenantAddonDomainContext.Aggregates.Branches.Events;

public record BranchCreated(string Name) : ICreatedEventPayload;
