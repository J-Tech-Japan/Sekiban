using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Orleans.Parts;

public static class OrleansEventExtensions
{
    public static List<IEvent> ToEventsAndReplaceTime(this List<IEvent> events, IEventTypes eventTypes) =>
        events
            .Select(
                e => eventTypes.GenerateTypedEvent(
                    e.GetPayload(),
                    e.PartitionKeys,
                    SortableUniqueIdValue.Generate(DateTime.UtcNow, e.Id),
                    e.Version,
                    e.Metadata))
            .Where(result => result.IsSuccess)
            .Select(result => result.GetValue())
            .ToList();
}