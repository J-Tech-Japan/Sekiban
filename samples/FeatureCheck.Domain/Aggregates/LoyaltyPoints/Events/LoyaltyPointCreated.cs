using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointCreated(int InitialPoint) : IEventPayload<LoyaltyPoint, LoyaltyPointCreated>
{
    public static LoyaltyPoint OnEvent(LoyaltyPoint aggregatePayload, Event<LoyaltyPointCreated> ev) => new(ev.Payload.InitialPoint, null, false);
    public LoyaltyPoint OnEventInstance(LoyaltyPoint aggregatePayload, Event<LoyaltyPointCreated> ev) => OnEvent(aggregatePayload, ev);
}
