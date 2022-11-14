using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointCreated(int InitialPoint) : ICreatedEvent<LoyaltyPoint>
{
    public LoyaltyPoint OnEvent(LoyaltyPoint payload, IEvent ev) => new LoyaltyPoint(InitialPoint, null, false);
}
