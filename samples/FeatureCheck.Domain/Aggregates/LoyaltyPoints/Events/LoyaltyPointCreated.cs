using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointCreated(int InitialPoint) : IEventPayload<LoyaltyPoint>
{
    public LoyaltyPoint OnEvent(LoyaltyPoint payload, IEvent ev) => new LoyaltyPoint(InitialPoint, null, false);
}
