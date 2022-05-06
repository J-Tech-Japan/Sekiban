using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointUsed(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointUsageTypeKeys Reason,
    int PointAmount,
    string Note) : ChangeAggregateEvent<LoyaltyPoint>(ClientId);
