using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Event;

namespace Customer.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointUsed
(DateTime HappenedDate, LoyaltyPointUsageTypeKeys Reason, int PointAmount,
    string Note) : IEventPayload<LoyaltyPoint>
{
    public LoyaltyPoint OnEvent(LoyaltyPoint payload, IEvent ev)
    {
        return payload with { CurrentPoint = payload.CurrentPoint - PointAmount, LastOccuredTime = HappenedDate };
    }
}