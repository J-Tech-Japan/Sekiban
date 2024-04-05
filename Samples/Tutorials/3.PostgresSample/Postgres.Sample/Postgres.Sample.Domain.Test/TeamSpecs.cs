using Postgres.Sample.Domain.Aggregates.Teams;
using Sekiban.Testing.SingleProjections;
namespace Postgres.Sample.Domain.Test;

public class TeamSpecs : AggregateTest<Team, DomainDependency>
{
    [Fact]
    public void RegisterTeam()
    {
        WhenCommand(new RegisterTeam("Dogers"));

        ThenPayloadIs(new Team("Dogers"));
    }
}