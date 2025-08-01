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
