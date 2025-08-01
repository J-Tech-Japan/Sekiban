# DCB Interfaces

This document defines the core interfaces for the DCB (Dynamic Consistency Boundary) system.

## Event Interfaces

### IEventPayload

```csharp
public interface IEventPayload
{
    // Empty interface for event payload marker
}
```

Base interface for all event payloads in the system. All event data must implement this interface.

## Tag Interfaces

### ITag

```csharp
public interface ITag
{
    string IsConsistencyTag();
    string GetTagGroup();
    string GetTag();
}
```

Interface for tag implementations that define consistency boundaries and tag grouping.

### ITagStatePayload

```csharp
public interface ITagStatePayload
{
    // Empty interface for tag state payload marker
}
```

Base interface for all tag state payloads in the system.

### ITagProjector

```csharp
public interface ITagProjector
{
    string GetTagProjectorName();
    TagState Project(TagState current, Event ev);
}
```

Interface for projecting events onto tag states to build read models.

## Actor Interfaces

### ITagStateActorCommon

```csharp
public interface ITagStateActorCommon
{
    SerializableTagState GetState();
    string GetTagStateActorId();
}
```

Common interface for tag state actors providing state access and identification.

### ITagConsistentActorCommon

```csharp
public interface ITagConsistentActorCommon
{
    ResultBox<TagWriteReservation> MakeReservation(string lastSortableUniqueId);
    bool ConfirmReservation(TagWriteReservation reservation);
    bool CancelReservation(TagWriteReservation reservation);
}
```

Common interface for tag consistent actors managing write reservations.
