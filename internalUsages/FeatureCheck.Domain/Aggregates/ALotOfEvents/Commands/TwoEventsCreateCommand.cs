using FeatureCheck.Domain.Aggregates.ALotOfEvents.Events;
using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.ALotOfEvents.Commands;

public record TwoEventsCreateCommand(Guid AggregateId)
    : ICommandWithHandler<ALotOfEventsAggregate, TwoEventsCreateCommand>
{
    public Guid GetAggregateId() => AggregateId;

    public static ResultBox<UnitValue> HandleCommand(TwoEventsCreateCommand command,
        ICommandContext<ALotOfEventsAggregate> context) =>
        context.AppendEvents(new ALotOfEventsSingleEvent("0"), new ALotOfEventsSingleEvent("1"));
}
