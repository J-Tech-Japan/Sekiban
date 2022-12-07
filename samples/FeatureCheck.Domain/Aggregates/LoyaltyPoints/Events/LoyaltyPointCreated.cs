using Sekiban.Core.Event;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointCreated(int InitialPoint) : IEventPayload<LoyaltyPoint>
{
    public LoyaltyPoint OnEvent(LoyaltyPoint payload, IEvent ev)
    {
        return new(InitialPoint, null, false);
    }
}
