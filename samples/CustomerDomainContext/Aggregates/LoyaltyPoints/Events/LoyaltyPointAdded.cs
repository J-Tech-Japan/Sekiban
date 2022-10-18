using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Event;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointAdded
    (DateTime HappenedDate, LoyaltyPointReceiveTypeKeys Reason, int PointAmount, string Note) : IChangedAggregateEventPayload<LoyaltyPoint>;
