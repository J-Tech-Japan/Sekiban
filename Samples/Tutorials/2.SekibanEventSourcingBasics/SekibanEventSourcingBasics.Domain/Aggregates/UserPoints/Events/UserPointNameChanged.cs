using Sekiban.Core.Events;
namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Events;

public record UserPointNameChanged(string ChangedName) : IEventPayload<UserPoint, UserPointNameChanged>
{
    public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointNameChanged> ev) =>
        aggregatePayload with { Name = ev.Payload.ChangedName };
}