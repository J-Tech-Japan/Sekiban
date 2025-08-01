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
