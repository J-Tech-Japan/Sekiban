using ResultBoxes;
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
    public static Team OnEvent(Team aggregatePayload, Event<TeamNameChanged> ev) =>
        aggregatePayload with { Name = ev.Payload.Name };
}
public record ChangeTeamName(Guid TeamId, string Name) : ICommandWithHandler<Team, ChangeTeamName>
{
    public Guid GetAggregateId() => TeamId;
    public static ResultBox<UnitValue> HandleCommand(
        ChangeTeamName command,
        ICommandContext<Team> context) => context.AppendEvent(new TeamNameChanged(command.Name));
}
