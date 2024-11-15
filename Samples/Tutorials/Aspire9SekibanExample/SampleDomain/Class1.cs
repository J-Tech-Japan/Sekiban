using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Events;
using System.Reflection;
namespace SampleDomain;

public class DomainDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    public override void Define() { }
}
public record BaseballTeam(string Name, string City, string League) : IAggregatePayload<BaseballTeam>
{
    public static BaseballTeam CreateInitialPayload(BaseballTeam? _) => new(string.Empty, string.Empty, string.Empty);
}
public record BaseballTeamCreated(string Name, string City, string League)
    : IEventPayload<BaseballTeam, BaseballTeamCreated>
{
    public static BaseballTeam OnEvent(BaseballTeam aggregatePayload, Event<BaseballTeamCreated> ev) =>
        aggregatePayload with { Name = ev.Payload.Name, City = ev.Payload.City, League = ev.Payload.League };
}
public record CreateBaseballTeam(string Name, string City, string League)
    : ICommandWithHandler<BaseballTeam, CreateBaseballTeam>
{

    public static Guid SpecifyAggregateId(CreateBaseballTeam command) => Guid.CreateVersion7();
    public static ResultBox<EventOrNone<BaseballTeam>> HandleCommand(
        CreateBaseballTeam command,
        ICommandContext<BaseballTeam> context) =>
        EventOrNone.Event(new BaseballTeamCreated(command.Name, command.City, command.League));
}
