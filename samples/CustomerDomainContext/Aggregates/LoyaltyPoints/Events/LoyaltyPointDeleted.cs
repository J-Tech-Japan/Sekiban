namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointDeleted(Guid ClientId) : ChangeAggregateEvent<LoyaltyPoint>(ClientId);
