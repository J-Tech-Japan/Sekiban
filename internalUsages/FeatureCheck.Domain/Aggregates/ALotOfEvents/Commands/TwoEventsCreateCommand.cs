using FeatureCheck.Domain.Aggregates.ALotOfEvents.Events;
using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.ALotOfEvents.Commands;

public record TwoEventsCreateCommand(Guid AggregateId)
    : ICommandWithHandler<ALotOfEventsAggregate, TwoEventsCreateCommand>
{
    public static ResultBox<UnitValue> HandleCommand(
        TwoEventsCreateCommand command,
        ICommandContext<ALotOfEventsAggregate> context) =>
        context.AppendEvents(new ALotOfEventsSingleEvent("0"), new ALotOfEventsSingleEvent("1"));
    public static Guid SpecifyAggregateId(TwoEventsCreateCommand command) => command.AggregateId;
}
