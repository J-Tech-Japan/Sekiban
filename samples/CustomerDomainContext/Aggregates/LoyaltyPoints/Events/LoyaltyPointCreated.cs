namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointCreated(int InitialPoint) : ICreatedEventPayload;
