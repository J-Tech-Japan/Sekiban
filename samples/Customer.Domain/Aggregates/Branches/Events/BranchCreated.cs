using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Branches.Events;

public record BranchCreated(string Name) : ICreatedAggregateEventPayload<Branch>;
