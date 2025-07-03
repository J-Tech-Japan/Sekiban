# Dapr Actors Implementation Design for Sekiban TypeScript

## Overview

This document describes the exact implementation requirements for Dapr actors in Sekiban TypeScript, mirroring the C# Sekiban.Pure.Dapr implementation. The implementation must provide three core actors that work together to provide distributed event sourcing capabilities.

## Actor Architecture

### 1. AggregateActor

**Purpose**: Main actor for handling aggregate state management and command execution. Equivalent to Orleans' AggregateProjectorGrain.

**Key Responsibilities**:
- Manages aggregate state with snapshot + delta pattern
- Executes commands and produces events
- Provides lazy initialization
- Automatic periodic state saving

**Required Methods**:

```typescript
interface IAggregateActor {
  // Get current aggregate state
  getAggregateStateAsync(): Promise<SerializableAggregate>;
  
  // Execute command and return response
  executeCommandAsync(command: SerializableCommandAndMetadata): Promise<string>; // JSON response
  
  // Rebuild state from all events
  rebuildStateAsync(): Promise<void>;
  
  // Timer callback for periodic saving
  saveStateCallbackAsync(state?: any): Promise<void>;
  
  // Reminder handling (IRemindable)
  receiveReminderAsync(
    reminderName: string,
    state: Buffer,
    dueTime: TimeSpan,
    period: TimeSpan
  ): Promise<void>;
}
```

**Internal Implementation Details**:

```typescript
class AggregateActor extends AbstractActor implements IAggregateActor {
  private initialized = false;
  private hasUnsavedChanges = false;
  private saveTimer?: NodeJS.Timer;
  
  // State keys
  private readonly AGGREGATE_STATE_KEY = "aggregateState";
  private readonly PARTITION_INFO_KEY = "partitionInfo";
  
  async onActivate(): Promise<void> {
    // Set up 10-second save timer
    this.saveTimer = setInterval(() => this.saveStateCallbackAsync(), 10000);
  }
  
  async onDeactivate(): Promise<void> {
    if (this.saveTimer) {
      clearInterval(this.saveTimer);
    }
    await this.saveStateAsync();
  }
  
  private async ensureInitializedAsync(): Promise<void> {
    if (!this.initialized) {
      // Lazy load state on first use
      await this.loadStateInternalAsync();
      this.initialized = true;
    }
  }
  
  private async loadStateInternalAsync(
    eventHandler: IAggregateEventHandlerActor
  ): Promise<void> {
    // 1. Try to load snapshot from state
    const snapshot = await this.stateManager.tryGetState(this.AGGREGATE_STATE_KEY);
    
    if (snapshot[0]) {
      // 2. Load delta events since snapshot
      const deltaEvents = await eventHandler.getDeltaEventsAsync(
        snapshot[1].lastSortableUniqueId,
        1000
      );
      
      // 3. Apply delta events to snapshot
      // ... projection logic
    } else {
      // 4. No snapshot - rebuild from all events
      const allEvents = await eventHandler.getAllEventsAsync();
      // ... full rebuild logic
    }
  }
}
```

**State Management Pattern**:
- Stores aggregate state as snapshot in Dapr state
- Tracks last processed event ID for delta loading
- Uses `hasUnsavedChanges` flag to optimize saves
- Periodic saving every 10 seconds via timer

### 2. AggregateEventHandlerActor

**Purpose**: Handles event persistence and retrieval for aggregate streams. Equivalent to Orleans' AggregateEventHandlerGrain.

**Key Responsibilities**:
- Append events with optimistic concurrency control
- Retrieve events (all or delta)
- Maintain event ordering
- Fallback to external storage when needed

**Required Methods**:

```typescript
interface IAggregateEventHandlerActor {
  // Append events with concurrency check
  appendEventsAsync(
    expectedLastSortableUniqueId: string,
    events: SerializableEventDocument[]
  ): Promise<EventHandlingResponse>;
  
  // Get events after a specific point
  getDeltaEventsAsync(
    fromSortableUniqueId: string,
    limit: number
  ): Promise<SerializableEventDocument[]>;
  
  // Get all events
  getAllEventsAsync(): Promise<SerializableEventDocument[]>;
  
  // Get last event ID
  getLastSortableUniqueIdAsync(): Promise<string>;
  
  // Register projector (currently no-op)
  registerProjectorAsync(projectorKey: string): Promise<void>;
}
```

**Internal Implementation Details**:

```typescript
class AggregateEventHandlerActor extends AbstractActor 
  implements IAggregateEventHandlerActor {
  
  // State keys
  private readonly HANDLER_STATE_KEY = "aggregateEventHandler";
  private readonly EVENTS_KEY = "aggregateEventDocuments";
  
  async appendEventsAsync(
    expectedLastSortableUniqueId: string,
    events: SerializableEventDocument[]
  ): Promise<EventHandlingResponse> {
    // 1. Load current state
    const [hasState, handlerState] = await this.stateManager.tryGetState(
      this.HANDLER_STATE_KEY
    );
    
    // 2. Validate optimistic concurrency
    if (handlerState?.lastSortableUniqueId !== expectedLastSortableUniqueId) {
      return {
        isSuccess: false,
        error: "Concurrency conflict"
      };
    }
    
    // 3. Append to state
    const currentEvents = await this.loadEventsFromState();
    currentEvents.push(...events);
    
    // 4. Save to both state and external storage
    await this.stateManager.setState(this.EVENTS_KEY, currentEvents);
    await this.eventWriter.saveEvents(events);
    
    // 5. Update metadata
    const newLastId = events[events.length - 1].sortableUniqueId;
    await this.stateManager.setState(this.HANDLER_STATE_KEY, {
      lastSortableUniqueId: newLastId
    });
    
    return { isSuccess: true };
  }
  
  private async loadEventsFromState(): Promise<SerializableEventDocument[]> {
    const [hasEvents, events] = await this.stateManager.tryGetState(this.EVENTS_KEY);
    
    if (!hasEvents || events.length === 0) {
      // Fallback to external storage
      const partitionKeys = this.getPartitionKeys();
      return await this.eventReader.getEvents(partitionKeys);
    }
    
    return events;
  }
}
```

**State Management Pattern**:
- Stores event metadata (last ID) separately from events
- Falls back to external storage (IEventReader/Writer) when state is empty
- Maintains both actor state and external storage in sync

### 3. MultiProjectorActor

**Purpose**: Handles cross-aggregate projections and queries over multiple aggregates.

**Key Responsibilities**:
- Execute queries (single and list)
- Handle events from multiple aggregates via PubSub
- Maintain safe/unsafe state for eventual consistency
- Buffer and order events before processing

**Required Methods**:

```typescript
interface IMultiProjectorActor {
  // Execute single-item query
  queryAsync(query: SerializableQuery): Promise<QueryResponse>;
  
  // Execute list query
  queryListAsync(query: SerializableListQuery): Promise<ListQueryResponse>;
  
  // Check if event has been processed
  isSortableUniqueIdReceived(sortableUniqueId: string): Promise<boolean>;
  
  // Build current state from buffer
  buildStateAsync(): Promise<MultiProjectionState>;
  
  // Rebuild state from scratch
  rebuildStateAsync(): Promise<void>;
  
  // Handle published event
  handlePublishedEvent(envelope: DaprEventEnvelope): Promise<void>;
}
```

**Internal Implementation Details**:

```typescript
class MultiProjectorActor extends AbstractActor implements IMultiProjectorActor {
  private safeState?: MultiProjectionState;  // State older than 7 seconds
  private unsafeState?: MultiProjectionState; // Recent state
  private eventBuffer: BufferedEvent[] = [];
  private lastProcessedTimestamp?: Date;
  
  // Reminder names
  private readonly SNAPSHOT_REMINDER = "snapshot";
  private readonly EVENT_CHECK_REMINDER = "eventCheck";
  
  async onActivate(): Promise<void> {
    // Set up reminders
    await this.registerReminderAsync(
      this.SNAPSHOT_REMINDER,
      Buffer.from(""),
      "PT5M",  // 5 minutes
      "PT5M"
    );
    
    await this.registerReminderAsync(
      this.EVENT_CHECK_REMINDER,
      Buffer.from(""),
      "PT1S",  // 1 second
      "PT1S"
    );
  }
  
  async handlePublishedEvent(envelope: DaprEventEnvelope): Promise<void> {
    // Add to buffer with timestamp
    this.eventBuffer.push({
      event: envelope.event,
      receivedAt: new Date()
    });
    
    // Sort buffer by sortableUniqueId
    this.eventBuffer.sort((a, b) => 
      a.event.sortableUniqueId.localeCompare(b.event.sortableUniqueId)
    );
  }
  
  private async flushBuffer(): Promise<void> {
    const now = new Date();
    const safeWindowMs = 7000; // 7 seconds
    
    // Process events older than safe window
    const safeEvents = this.eventBuffer.filter(
      e => now.getTime() - e.receivedAt.getTime() > safeWindowMs
    );
    
    if (safeEvents.length > 0) {
      // Update safe state
      this.safeState = await this.applyEventsToState(
        this.safeState,
        safeEvents.map(e => e.event)
      );
      
      // Remove processed events from buffer
      this.eventBuffer = this.eventBuffer.filter(
        e => !safeEvents.includes(e)
      );
    }
    
    // Always update unsafe state with all buffered events
    this.unsafeState = await this.applyEventsToState(
      this.safeState,
      this.eventBuffer.map(e => e.event)
    );
  }
  
  async queryAsync(query: SerializableQuery): Promise<QueryResponse> {
    await this.flushBuffer();
    
    // Use safe state for queries by default
    const state = this.safeState || await this.buildStateAsync();
    
    // Execute query against state
    return this.executeQuery(query, state);
  }
  
  async receiveReminderAsync(
    reminderName: string,
    state: Buffer,
    dueTime: TimeSpan,
    period: TimeSpan
  ): Promise<void> {
    switch (reminderName) {
      case this.SNAPSHOT_REMINDER:
        await this.handleSnapshotReminder();
        break;
      case this.EVENT_CHECK_REMINDER:
        await this.handleEventCheckReminder();
        break;
    }
  }
  
  private async handleSnapshotReminder(): Promise<void> {
    // Persist current state
    if (this.safeState) {
      await this.persistStateAsync(this.safeState);
    }
  }
  
  private async handleEventCheckReminder(): Promise<void> {
    // Catch up from external storage
    await this.catchUpFromStoreAsync();
    
    // Flush buffer
    await this.flushBuffer();
  }
}
```

**State Management Pattern**:
- **Dual State**: Maintains `safeState` (>7 seconds old) and `unsafeState` (recent)
- **Event Buffer**: Buffers incoming events, sorted by sortableUniqueId
- **Safe Window**: 7-second delay for eventual consistency
- **Reminders**: Snapshot every 5 minutes, event check every 1 second
- **Fallback to Timers**: Uses timers if reminders fail

## Key Implementation Patterns

### 1. Snapshot + Delta Pattern (AggregateActor)
- Load snapshot from state
- Query only events after snapshot
- Apply delta events to rebuild current state
- Periodically save new snapshots

### 2. Optimistic Concurrency (AggregateEventHandlerActor)
- Client provides expected last event ID
- Actor validates before appending
- Returns error on mismatch

### 3. Safe/Unsafe State (MultiProjectorActor)
- Safe state: Events older than 7 seconds
- Unsafe state: All events including recent
- Queries use safe state by default
- Provides eventual consistency guarantee

### 4. Event Ordering
- All events sorted by SortableUniqueId
- Guarantees deterministic replay
- Handles out-of-order delivery

### 5. Lazy Initialization
- Actors defer state loading until first use
- Reduces activation overhead
- Improves cold start performance

## Serialization Requirements

All actor methods must use serializable types for Dapr JSON serialization:

```typescript
// Command execution returns JSON string
executeCommandAsync(command: SerializableCommandAndMetadata): Promise<string>;

// All DTOs must be JSON-serializable
interface SerializableAggregate {
  partitionKeys: PartitionKeys;
  aggregate: Aggregate;
  lastSortableUniqueId: string;
}

interface SerializableEventDocument {
  id: string;
  sortableUniqueId: string;
  payload: any; // JSON object
  metadata: EventMetadata;
  // ... other fields
}
```

## Error Handling

1. **Concurrency Conflicts**: Return error response, don't throw
2. **State Loading Failures**: Fall back to external storage
3. **Reminder Failures**: Fall back to timers
4. **Serialization Errors**: Log and return error response

## Performance Considerations

1. **Batch Event Loading**: Load events in chunks (default 1000)
2. **State Caching**: Cache state within actor instance
3. **Lazy Loading**: Defer initialization until needed
4. **Periodic Saves**: Balance between durability and performance

## Testing Strategy

1. **Unit Tests**: Test each actor method independently
2. **Integration Tests**: Test actor interactions
3. **Concurrency Tests**: Test optimistic locking
4. **Performance Tests**: Measure snapshot vs full rebuild
5. **Resilience Tests**: Test reminder/timer fallbacks

## Migration from Current Implementation

The current DaprAggregateActor in the TypeScript implementation needs to be updated to match this exact pattern:

1. Add AggregateEventHandlerActor for event management
2. Add MultiProjectorActor for projections
3. Update AggregateActor to use snapshot + delta pattern
4. Implement all required methods with exact signatures
5. Add reminder support with timer fallback
6. Implement safe/unsafe state pattern

This will ensure full compatibility with the C# implementation and provide the same distributed event sourcing capabilities.