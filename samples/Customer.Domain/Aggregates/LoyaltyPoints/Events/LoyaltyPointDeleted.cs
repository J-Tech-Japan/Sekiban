using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointDeleted : IChangedEvent<LoyaltyPoint>
{
    public LoyaltyPoint OnEvent(LoyaltyPoint payload, IAggregateEvent aggregateEvent)
    {
        return payload with { IsDeleted = true };
    }
}
