using Sekiban.Core.Events;
namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Events;

public record UserPointUsed(int Point, string Note) : IEventPayload<UserPoint, UserPointUsed>
{
    public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointUsed> ev) =>
        aggregatePayload with { Point = aggregatePayload.Point - ev.Payload.Point };
}
