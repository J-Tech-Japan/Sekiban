using ResultBoxes;
using Sekiban.Core.Command;
namespace Postgres.Sample.Domain.Aggregates.Teams;

public record RegisterTeam(string Name) : ICommandWithHandler<Team, RegisterTeam>
{
    public static Guid SpecifyAggregateId(RegisterTeam command) => Guid.CreateVersion7();
    public static ResultBox<EventOrNone<Team>> HandleCommand(RegisterTeam command, ICommandContext<Team> context) =>
        context.AppendEvent(new TeamRegistered(command.Name));
}
