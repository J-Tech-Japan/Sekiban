using Sekiban.Core.Events;
namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Events;

public record UserPointCreated(string Name, string Email, int Point)
    : IEventPayload<UserPoint, UserPointCreated>
{
    public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointCreated> ev) =>
        new(ev.Payload.Name, ev.Payload.Email, ev.Payload.Point);
}
