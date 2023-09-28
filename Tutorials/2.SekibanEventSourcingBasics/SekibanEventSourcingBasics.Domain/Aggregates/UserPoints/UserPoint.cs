using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;

namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints;

public record UserPoint(string Name, string Email, int Point) : IAggregatePayload<UserPoint>
{
    public static UserPoint CreateInitialPayload(UserPoint? _) => new UserPoint(string.Empty,string.Empty,0);
}

public record UserPointCreated(string Name, string Email, int Point) : IEventPayload<UserPoint, UserPointCreated>
{
    public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointCreated> ev) =>
        new(ev.Payload.Name, ev.Payload.Email, ev.Payload.Point);
}

public record UserPointNameChanged(string ChangedName) : IEventPayload<UserPoint, UserPointNameChanged>
{
    public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointNameChanged> ev) =>
        aggregatePayload with { Name = ev.Payload.ChangedName };
}

public record UserPointReceived(int Point, string Note) : IEventPayload<UserPoint, UserPointReceived>
{
    public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointReceived> ev) =>
        aggregatePayload with { Point = aggregatePayload.Point + ev.Payload.Point };
}

public record UserPointUsed(int Point, string Note) : IEventPayload<UserPoint, UserPointUsed>
{
    public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointUsed> ev) =>
        aggregatePayload with { Point = aggregatePayload.Point - ev.Payload.Point };
}