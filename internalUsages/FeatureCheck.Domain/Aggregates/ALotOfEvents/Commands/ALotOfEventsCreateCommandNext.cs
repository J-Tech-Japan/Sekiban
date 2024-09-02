using FeatureCheck.Domain.Aggregates.ALotOfEvents.Events;
using ResultBoxes;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.ALotOfEvents.Commands;

public record ALotOfEventsCreateCommandNext(Guid AggregateId, int NumberOfEvents)
    : ICommandWithHandler<ALotOfEventsAggregate, ALotOfEventsCreateCommandNext>
{
    public static ResultBox<UnitValue> HandleCommand(
        ALotOfEventsCreateCommandNext command,
        ICommandContext<ALotOfEventsAggregate> context)
    {
        return ResultBox
            .FromValue(
                Enumerable
                    .Range(1, command.NumberOfEvents)
                    .Select(
                        i => (IEventPayloadApplicableTo<ALotOfEventsAggregate>)new ALotOfEventsSingleEvent(
                            i.ToString()))
                    .ToArray())
            .Conveyor(context.AppendEvents);
    }
    public static Guid SpecifyAggregateId(ALotOfEventsCreateCommandNext command) => command.AggregateId;
}
