namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointCreated(
    Guid ClientId,
    int InitialPoint
) : CreateAggregateEvent<LoyaltyPoint>(
    ClientId
);
