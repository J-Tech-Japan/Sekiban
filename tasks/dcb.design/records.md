# DCB Records

This document highlights the immutable record types used throughout the current DCB (Dynamic Consistency Boundary) runtime.

## SortableUniqueId
```csharp
public record SortableUniqueId(string Value);
```

A 30-digit sortable identifier composed of UTC ticks (first 19 digits) and an 11-digit pseudo-random suffix. Helper methods generate safe ids, extract timestamps, and validate formatting (src/Sekiban.Dcb/Common/SortableUniqueId.cs).

## EventMetadata
```csharp
public record EventMetadata(string CausationId, string CorrelationId, string ExecutedUser);
```

Minimal metadata stored alongside each event for traceability. The executor currently uses the command type name as the correlation id and `GeneralSekibanExecutor` as the executed user.

## Event
```csharp
public record Event(
    IEventPayload Payload,
    string SortableUniqueIdValue,
    string EventType,
    Guid Id,
    EventMetadata EventMetadata,
    List<string> Tags);
```

The canonical event that `IEventStore` implementations persist. `Tags` holds the string form `"[group]:[content]"` for every affected tag.

## EventPayloadWithTags
```csharp
public record EventPayloadWithTags(IEventPayload Event, List<ITag> Tags);
```

A convenience wrapper produced by command handlers to associate a payload with its tags prior to serialization.

## TagStream
```csharp
public record TagStream(string Tag, Guid EventId, string SortableUniqueId);
```

Represents the link between a tag and an event in persistence, enabling efficient tag-based lookups.

## TagState
```csharp
public record TagState(
    ITagStatePayload Payload,
    int Version,
    string LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector,
    string ProjectorVersion = "");
```

The in-memory projection of a tag. `Version` reflects the number of events applied, `LastSortedUniqueId` is the cursor used for optimistic reads, and `ProjectorVersion` tracks projector changes.

## TagStateTyped
```csharp
public record TagStateTyped<TPayload>(ITag Tag, TPayload Payload, long Version, DateTimeOffset LastModified)
    where TPayload : ITagStatePayload;
```

`GeneralCommandContext` uses this record to return strongly typed projections along with the version number and retrieval timestamp.

## SerializableEvent
```csharp
public record SerializableEvent(
    byte[] Payload,
    string SortableUniqueIdValue,
    Guid Id,
    EventMetadata EventMetadata,
    List<string> Tags,
    string EventPayloadName);
```

Used by persistence layers that require serialized payloads. The name aids dynamic deserialization.

## SerializableTagState
```csharp
public record SerializableTagState(
    byte[] Payload,
    int Version,
    string LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector,
    string TagPayloadName,
    string ProjectorVersion);
```

Snapshot form of `TagState` for durable storage or inter-actor communication.

## TagWriteReservation
```csharp
public record TagWriteReservation(string ReservationCode, string ExpiredUTC, string Tag);
```

Represents an in-memory reservation for a tag. `ExpiredUTC` is stored as an ISO-8601 string and interpreted by `GeneralTagConsistentActor` when cleaning up stale reservations.
