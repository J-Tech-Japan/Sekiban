using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Orleans.Grains;

public record AggregateEventHandlerGrainToPersist
{
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public OptionalValue<DateTime> LastEventDate { get; init; } = OptionalValue<DateTime>.Empty;
    public static AggregateEventHandlerGrainToPersist FromEvents(IEnumerable<IEvent> events)
    {
        var lastEvent = events.LastOrDefault();
        var last = lastEvent?.SortableUniqueId ?? string.Empty;
        var value = new SortableUniqueIdValue(last);
        if (string.IsNullOrWhiteSpace(last))
            return new AggregateEventHandlerGrainToPersist
                { LastSortableUniqueId = string.Empty, LastEventDate = OptionalValue<DateTime>.Empty };
        return new AggregateEventHandlerGrainToPersist
            { LastSortableUniqueId = last, LastEventDate = value.GetTicks() };
    }
}
