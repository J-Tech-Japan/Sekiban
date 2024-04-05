using Sekiban.Core.Events;
namespace Postgres.Sample.Domain.Aggregates.Players;

public record PlayerRegisteredToTeam(Guid TeamId) : IEventPayload<Player, PlayerRegisteredToTeam>
{
    public static Player OnEvent(Player aggregatePayload, Event<PlayerRegisteredToTeam> ev) =>
        aggregatePayload with { TeamId = ev.Payload.TeamId };
}
