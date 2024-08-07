using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.ALotOfEvents.Events;

public record ALotOfEventsSingleEvent(string Note) : IEventPayload<ALotOfEventsAggregate, ALotOfEventsSingleEvent>
{
    public static ALotOfEventsAggregate OnEvent(
        ALotOfEventsAggregate aggregatePayload,
        Event<ALotOfEventsSingleEvent> ev) =>
        new()
        {
            Count = aggregatePayload.Count + 1
        };
}
