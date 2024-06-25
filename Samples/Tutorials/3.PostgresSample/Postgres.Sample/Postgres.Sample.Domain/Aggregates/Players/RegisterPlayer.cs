using ResultBoxes;
using Sekiban.Core.Command;
namespace Postgres.Sample.Domain.Aggregates.Players;

public record RegisterPlayer(string Name, Guid TeamId)
    : ICommandWithHandlerForExistingAggregate<Player, RegisterPlayer>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public static ResultBox<UnitValue> HandleCommand(
        RegisterPlayer command,
        ICommandContext<Player> context) => context.AppendEvent(new PlayerRegistered(command.Name))
        .Conveyor(_ => context.AppendEvent(new PlayerRegisteredToTeam(command.TeamId)));
}
