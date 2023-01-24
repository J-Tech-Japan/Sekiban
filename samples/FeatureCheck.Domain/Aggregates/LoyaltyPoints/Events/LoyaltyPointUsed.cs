using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointUsed
(
    DateTime HappenedDate,
    LoyaltyPointUsageTypeKeys Reason,
    int PointAmount,
    string Note) : IEventPayload<LoyaltyPoint, LoyaltyPointUsed>
{
    public static LoyaltyPoint OnEvent(LoyaltyPoint payload, Event<LoyaltyPointUsed> ev) =>
        payload with { CurrentPoint = payload.CurrentPoint - ev.Payload.PointAmount, LastOccuredTime = ev.Payload.HappenedDate };
    public LoyaltyPoint OnEventInstance(LoyaltyPoint payload, Event<LoyaltyPointUsed> ev) => OnEvent(payload, ev);
}
