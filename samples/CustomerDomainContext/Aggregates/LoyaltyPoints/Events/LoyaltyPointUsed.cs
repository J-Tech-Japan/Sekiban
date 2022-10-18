using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Event;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointUsed
    (DateTime HappenedDate, LoyaltyPointUsageTypeKeys Reason, int PointAmount, string Note) : IChangedAggregateEventPayload<LoyaltyPoint>;
