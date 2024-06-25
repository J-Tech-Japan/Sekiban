using Sekiban.Core.Events;
namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Events;

public record UserPointReceived(int Point, string Note)
    : IEventPayload<UserPoint, UserPointReceived>
{
    public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointReceived> ev) =>
        aggregatePayload with { Point = aggregatePayload.Point + ev.Payload.Point };
}
