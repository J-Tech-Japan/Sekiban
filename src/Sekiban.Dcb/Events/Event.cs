namespace Sekiban.Dcb.Events;

public record Event(
    IEventPayload Payload,
    string SortableUniqueIdValue,
    string EventType,
    Guid Id,
    EventMetadata EventMetadata,
    List<string> Tags);
