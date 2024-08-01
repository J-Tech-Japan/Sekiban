using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointUsed(DateTime HappenedDate, LoyaltyPointUsageTypeKeys Reason, int PointAmount, string Note)
    : IEventPayload<LoyaltyPoint, LoyaltyPointUsed>
{
    public static LoyaltyPoint OnEvent(LoyaltyPoint aggregatePayload, Event<LoyaltyPointUsed> ev) =>
        aggregatePayload with
        {
            CurrentPoint = aggregatePayload.CurrentPoint - ev.Payload.PointAmount, LastOccuredTime = ev.Payload.HappenedDate
        };
}
