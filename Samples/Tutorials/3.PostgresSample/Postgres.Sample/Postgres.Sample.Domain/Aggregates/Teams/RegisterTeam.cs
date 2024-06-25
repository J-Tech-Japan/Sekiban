using ResultBoxes;
using Sekiban.Core.Command;
namespace Postgres.Sample.Domain.Aggregates.Teams;

public record RegisterTeam(string Name) : ICommandWithHandler<Team, RegisterTeam>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public static ResultBox<UnitValue> HandleCommand(
        RegisterTeam command,
        ICommandContext<Team> context) => context.AppendEvent(new TeamRegistered(command.Name));
}
