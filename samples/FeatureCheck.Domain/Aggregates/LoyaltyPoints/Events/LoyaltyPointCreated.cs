using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointCreated(int InitialPoint) : IEventPayload<LoyaltyPoint, LoyaltyPointCreated>
{
    public LoyaltyPoint OnEventInstance(LoyaltyPoint payload, Event<LoyaltyPointCreated> ev) => OnEvent(payload, ev);
    public static LoyaltyPoint OnEvent(LoyaltyPoint payload, Event<LoyaltyPointCreated> ev) => new(ev.Payload.InitialPoint, null, false);
}
