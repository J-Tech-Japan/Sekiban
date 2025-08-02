namespace DcbLib.Events;

public record SerializableEvent(
    byte[] Payload,
    string SortableUniqueIdValue,
    Guid Id,
    EventMetadata EventMetadata,
    List<string> Tags,
    string EventPayloadName
);