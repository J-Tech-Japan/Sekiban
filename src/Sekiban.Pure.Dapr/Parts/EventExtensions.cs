using Sekiban.Pure.Events;
using Sekiban.Pure.Documents;

namespace Sekiban.Pure.Dapr.Parts;

/// <summary>
/// Extension methods for event processing in Dapr actors
/// </summary>
public static class EventExtensions
{
    /// <summary>
    /// Processes events by ensuring they have proper timestamps and sortable unique IDs
    /// </summary>
    public static List<IEvent> ToEventsAndReplaceTime(this List<IEvent> events, IEnumerable<Type> eventTypes)
    {
        var processedEvents = new List<IEvent>();
        var currentTime = DateTime.UtcNow;
        
        foreach (var ev in events)
        {
            // Create a new event with updated timestamp if needed
            var eventToAdd = ev;
            
            // If the event doesn't have a sortable unique ID, generate one
            if (string.IsNullOrWhiteSpace(ev.SortableUniqueId))
            {
                // Generate sortable unique ID based on timestamp
                var sortableId = SortableUniqueIdValue.Generate(
                    currentTime, 
                    Guid.NewGuid());
                
                // Create new event with sortable ID
                eventToAdd = new Event<IEventPayload>(
                    Id: Guid.NewGuid(),
                    Payload: ev.GetPayload(),
                    PartitionKeys: ev.PartitionKeys,
                    SortableUniqueId: sortableId,
                    Version: ev.Version,
                    Metadata: new EventMetadata(
                        CausationId: string.Empty,
                        CorrelationId: Guid.NewGuid().ToString(),
                        ExecutedUser: "system"));
            }
            
            processedEvents.Add(eventToAdd);
        }
        
        return processedEvents;
    }
}