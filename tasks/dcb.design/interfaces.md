# DCB Interfaces

This document defines the core interfaces for the DCB (Distributed Command Bus) system.

## Event Interfaces

### IEventPayload

```csharp
public interface IEventPayload
{
    // Empty interface for event payload marker
}
```

Base interface for all event payloads in the system. All event data must implement this interface.
