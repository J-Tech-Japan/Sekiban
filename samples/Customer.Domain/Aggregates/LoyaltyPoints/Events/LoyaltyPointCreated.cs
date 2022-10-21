using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointCreated(int InitialPoint) : ICreatedAggregateEventPayload<LoyaltyPoint>;
