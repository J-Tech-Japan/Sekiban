using Postgres.Sample.Domain.Aggregates.Players;
using Postgres.Sample.Domain.Aggregates.Teams;
using Sekiban.Core.Dependency;
using System.Reflection;
namespace Postgres.Sample.Domain;

public class DomainDependency : DomainDependencyDefinitionBase
{

    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    public override void Define()
    {
        AddAggregate<Team>()
            .AddCommandHandler<RegisterTeam, RegisterTeam.Handler>()
            .AddCommandHandler<ChangeTeamName,ChangeTeamName.Handler>();

        AddAggregate<Player>()
            .AddCommandHandler<RegisterPlayer, RegisterPlayer.Handler>();
    }
}
