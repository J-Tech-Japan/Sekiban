using Sekiban.Core.Event;
namespace CustomerDomainContext.Aggregates.Branches.Events;

public record BranchCreated(string Name) : ICreatedAggregateEventPayload<Branch>;
