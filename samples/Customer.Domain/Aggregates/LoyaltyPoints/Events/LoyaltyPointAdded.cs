using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointAdded
    (DateTime HappenedDate, LoyaltyPointReceiveTypeKeys Reason, int PointAmount, string Note) : IChangedAggregateEventPayload<LoyaltyPoint>;
