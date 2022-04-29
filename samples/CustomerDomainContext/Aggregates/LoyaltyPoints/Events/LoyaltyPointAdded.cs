using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointAdded(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointReceiveTypeKeys Reason,
    int PointAmount,
    string Note
) : ChangeAggregateEvent<LoyaltyPoint>(
    ClientId
);
