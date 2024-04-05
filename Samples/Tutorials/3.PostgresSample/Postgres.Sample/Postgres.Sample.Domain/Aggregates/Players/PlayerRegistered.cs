using Sekiban.Core.Events;
namespace Postgres.Sample.Domain.Aggregates.Players;

public record PlayerRegistered(string Name) : IEventPayload<Player, PlayerRegistered>
{
    public static Player OnEvent(Player aggregatePayload, Event<PlayerRegistered> ev) =>
        aggregatePayload with { Name = ev.Payload.Name };
}
