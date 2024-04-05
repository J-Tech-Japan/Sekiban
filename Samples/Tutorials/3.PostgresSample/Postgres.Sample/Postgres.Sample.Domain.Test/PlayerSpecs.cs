using Postgres.Sample.Domain.Aggregates.Players;
using Postgres.Sample.Domain.Aggregates.Teams;
using Sekiban.Testing.SingleProjections;
namespace Postgres.Sample.Domain.Test;

public class PlayerSpecs : AggregateTest<Player, DomainDependency>
{
    [Fact]
    public void RegisterPlayerSpec()
    {
        var teamId = GivenEnvironmentCommand(new RegisterTeam("Dogers"));

        WhenCommand(new RegisterPlayer("Shohei Ohtani", teamId));

        ThenPayloadIs(new Player("Shohei Ohtani", teamId));
    }
    [Fact]
    public void CanNotRegisterPlayerWhenTeamNotExists()
    {
        GivenEnvironmentCommand(new RegisterTeam("Dogers"));

        WhenCommand(new RegisterPlayer("Shohei Ohtani", Guid.NewGuid()));

        ThenThrows<ApplicationException>();
    }
}