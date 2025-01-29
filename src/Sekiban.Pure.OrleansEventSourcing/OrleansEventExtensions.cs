using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.OrleansEventSourcing;

public static class OrleansEventExtensions
{
    public static List<OrleansEvent> ToOrleansEvents(this List<IEvent> events) =>
        events.Select(OrleansEvent.FromEvent).ToList();
    
    public static ResultBox<IEvent> ToEvent(this OrleansEvent eventData, IEventTypes eventTypes)
    {
        return eventTypes.GenerateTypedEvent(
            eventData.Payload,
            eventData.PartitionKeys.ToPartitionKeys(),
            eventData.SortableUniqueId,
            eventData.Version);
    }
    
    public static List<IEvent> ToEvents(this List<OrleansEvent> events, IEventTypes eventTypes) =>
        events.Select(e => eventTypes.GenerateTypedEvent(e.Payload, e.PartitionKeys.ToPartitionKeys(), e.SortableUniqueId, e.Version))
            .Where(result => result.IsSuccess)
            .Select(result => result.GetValue()).ToList();
    
    public static List<IEvent> ToEventsAndReplaceTime(this List<OrleansEvent> events, IEventTypes eventTypes) =>
        events.Select(e => eventTypes.GenerateTypedEvent(e.Payload, e.PartitionKeys.ToPartitionKeys(), SortableUniqueIdValue.Generate(DateTime.UtcNow, e.Id), e.Version))
            .Where(result => result.IsSuccess)
            .Select(result => result.GetValue()).ToList();
}
