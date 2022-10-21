using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointDeleted : IChangedAggregateEventPayload<LoyaltyPoint>;
