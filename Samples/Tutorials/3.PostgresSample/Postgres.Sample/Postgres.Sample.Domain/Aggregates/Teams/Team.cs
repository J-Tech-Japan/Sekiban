using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace Postgres.Sample.Domain.Aggregates.Teams;

public record Team(string Name) : IAggregatePayload<Team>
{

    public static Team CreateInitialPayload(Team? _) => new(string.Empty);
}
public record TeamNameChanged(string Name) : IEventPayload<Team, TeamNameChanged>
{
    public static Team OnEvent(Team aggregatePayload, Event<TeamNameChanged> ev) => aggregatePayload with { Name = ev.Payload.Name };
}

public record ChangeTeamName(Guid TeamId, string Name) : ICommand<Team>
{
    public Guid GetAggregateId() => TeamId;

    public class Handler : ICommandHandler<Team, ChangeTeamName>
    {
        public IEnumerable<IEventPayloadApplicableTo<Team>> HandleCommand(
            ChangeTeamName command,
            ICommandContext<Team> context)
        {
            yield return new TeamNameChanged(command.Name);
        }
    }
}