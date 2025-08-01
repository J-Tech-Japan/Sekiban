# DCB Records

This document defines the core record types for the DCB (Dynamic Consistency Boundary) system.

## Overview

Records in DCB are immutable data structures used for:

- Commands
- Events
- State representations
- Tag metadata
- Reservation data

## Record Types

### SortableUniqueId

```csharp
public record SortableUniqueId(string Value);
```

A 30-digit value combining timestamp and random components, used throughout the Sekiban system for unique identification with sortable properties.

### EventMetadata

```csharp
public record EventMetadata(
    string CausationId, 
    string CorrelationId, 
    string ExecutedUser
);
```

Metadata associated with events for tracking causation, correlation, and user context.

### Event

```csharp
public record Event(
    IEventPayload Payload,
    string SortableUniqueIdValue,
    string EventType,
    Guid Id,
    EventMetadata EventMetadata,
    List<string> Tags
);
```

Core event record containing the event payload, associated metadata, and tags.

### TagStream

```csharp
public record TagStream(
    string Tag,
    Guid EventId,
    string SortableUniqueId
);
```

Links tags to events for efficient tag-based querying.

### TagState

```csharp
public record TagState(
    ITagStatePayload Payload,
    int Version,
    int LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector
);
```

Represents the current state of a tag with versioning and projection information.

### SerializableEvent

```csharp
public record SerializableEvent(
    byte[] Payload,
    string SortableUniqueIdValue,
    Guid Id,
    EventMetadata EventMetadata,
    List<string> Tags,
    string EventPayloadName
);
```

Serialized version of Event record with payload as byte array for storage.

### SerializableTagState

```csharp
public record SerializableTagState(
    byte[] Payload,
    int Version,
    int LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector,
    string TagPayloadName
);
```

Serialized version of TagState record with payload as byte array for storage.

### TagWriteReservation

```csharp
public record TagWriteReservation(
    string ReservationCode,
    string ExpiredUTC,
    string Tag
);
```

Represents a write reservation for a tag with expiration tracking.

### EventPayloadWithTags

```csharp
public record EventPayloadWithTags(
    IEventPayload Event,
    List<ITag> Tags
);
```

Groups an event payload with its associated tags for processing.
