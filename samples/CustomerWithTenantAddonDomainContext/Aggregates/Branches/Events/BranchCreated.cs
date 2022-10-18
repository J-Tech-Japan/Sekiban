using Sekiban.Core.Event;
namespace CustomerWithTenantAddonDomainContext.Aggregates.Branches.Events;

public record BranchCreated(string Name) : ICreatedEventPayload;
