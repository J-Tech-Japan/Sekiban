using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointAdded
(
    DateTime HappenedDate,
    LoyaltyPointReceiveTypeKeys Reason,
    int PointAmount,
    string Note) : IEventPayload<LoyaltyPoint, LoyaltyPointAdded>
{
    public LoyaltyPoint OnEventInstance(LoyaltyPoint payload, Event<LoyaltyPointAdded> ev) => OnEvent(payload, ev);
    public static LoyaltyPoint OnEvent(LoyaltyPoint payload, Event<LoyaltyPointAdded> ev) =>
        payload with { CurrentPoint = payload.CurrentPoint + ev.Payload.PointAmount, LastOccuredTime = ev.Payload.HappenedDate };
}
