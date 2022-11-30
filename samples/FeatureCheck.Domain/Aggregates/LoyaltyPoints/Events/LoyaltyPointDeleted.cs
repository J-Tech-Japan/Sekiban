using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointDeleted : IEventPayload<LoyaltyPoint>
{
    public LoyaltyPoint OnEvent(LoyaltyPoint payload, IEvent ev) => payload with { IsDeleted = true };
}
