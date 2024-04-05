using Postgres.Sample.Domain.Aggregates.Teams;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
namespace Postgres.Sample.Domain.Aggregates.Players;

public record RegisterPlayer(string Name, Guid TeamId) : ICommand<Player>
{

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler(IAggregateLoader aggregateLoader)
        : ICommandHandlerAsync<Player, RegisterPlayer>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<Player>> HandleCommandAsync(
            RegisterPlayer command,
            ICommandContext<Player> context)
        {
            var team = await aggregateLoader.AsDefaultStateAsync<Team>(command.TeamId);
            if (team is null)
            {
                throw new ApplicationException("Team not found.");
            }
            yield return new PlayerRegistered(command.Name);
            yield return new PlayerRegisteredToTeam(command.TeamId);
        }
    }
}
