using Postgres.Sample.Domain.Aggregates.Teams;
using ResultBoxes;
using Sekiban.Core.Command;
namespace Postgres.Sample.Domain.Aggregates.Players;

public record RegisterPlayer(string Name, Guid TeamId) : ICommandWithHandlerAsync<Player, RegisterPlayer>
{
    public static Guid SpecifyAggregateId(RegisterPlayer command) => Guid.CreateVersion7();
    public static Task<ResultBox<EventOrNone<Player>>> HandleCommandAsync(
        RegisterPlayer command,
        ICommandContext<Player> context) =>
        context
            .ExecuteQueryAsync(new ExistsTeam(command.TeamId))
            .Verify(exits => exits ? ExceptionOrNone.None : new ApplicationException("Team not exists"))
            .Conveyor(_ => context.AppendEvent(new PlayerRegistered(command.Name)))
            .Conveyor(_ => context.AppendEvent(new PlayerRegisteredToTeam(command.TeamId)));
}
