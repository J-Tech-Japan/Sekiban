using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace Postgres.Sample.Domain.Aggregates.Teams;

public record RegisterTeam(string Name) : ICommand<Team>
{

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<Team, RegisterTeam>
    {
        public IEnumerable<IEventPayloadApplicableTo<Team>> HandleCommand(
            RegisterTeam command,
            ICommandContext<Team> context)
        {
            yield return new TeamRegistered(command.Name);
        }
    }
}