using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointDeleted : IEventPayload<LoyaltyPoint, LoyaltyPointDeleted>
{
    public static LoyaltyPoint OnEvent(LoyaltyPoint aggregatePayload, Event<LoyaltyPointDeleted> ev) => aggregatePayload
        with
        {
            IsDeleted = true
        };
}
