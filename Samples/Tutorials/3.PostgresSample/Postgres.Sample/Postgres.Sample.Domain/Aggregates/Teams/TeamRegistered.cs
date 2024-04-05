using Sekiban.Core.Events;
namespace Postgres.Sample.Domain.Aggregates.Teams;

public record TeamRegistered(string Name) : IEventPayload<Team, TeamRegistered>
{
    public static Team OnEvent(Team aggregatePayload, Event<TeamRegistered> ev) => new(ev.Payload.Name);
}