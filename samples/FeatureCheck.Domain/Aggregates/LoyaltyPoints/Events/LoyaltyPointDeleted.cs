using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointDeleted : IEventPayload<LoyaltyPoint, LoyaltyPointDeleted>
{
    public LoyaltyPoint OnEventInstance(LoyaltyPoint payload, Event<LoyaltyPointDeleted> ev) => OnEvent(payload, ev);
    public static LoyaltyPoint OnEvent(LoyaltyPoint payload, Event<LoyaltyPointDeleted> ev) => payload with { IsDeleted = true };
}
