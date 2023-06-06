using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointAdded
    (DateTime HappenedDate, LoyaltyPointReceiveTypeKeys Reason, int PointAmount, string Note) : IEventPayload<LoyaltyPoint, LoyaltyPointAdded>
{
    public LoyaltyPoint OnEventInstance(LoyaltyPoint aggregatePayload, Event<LoyaltyPointAdded> ev) => OnEvent(aggregatePayload, ev);
    public static LoyaltyPoint OnEvent(LoyaltyPoint aggregatePayload, Event<LoyaltyPointAdded> ev) =>
        aggregatePayload with { CurrentPoint = aggregatePayload.CurrentPoint + ev.Payload.PointAmount, LastOccuredTime = ev.Payload.HappenedDate };
}
