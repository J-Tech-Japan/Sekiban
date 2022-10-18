using Sekiban.Core.Event;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointDeleted : IChangedAggregateEventPayload<LoyaltyPoint>;
