# Snapshot Architecture Design

## Overview

Sekiban TypeScript will use Dapr actors for snapshot state management. This design document outlines the architecture and implementation approach for Phase 12: Snapshot Management.

## Key Design Decisions

### 1. Snapshot Storage
- **Snapshots**: Stored in Dapr actor state (not in event stores)
- **Events**: Stored in PostgreSQL, CosmosDB, or in-memory stores
- **Separation of Concerns**: Clear distinction between event persistence and snapshot state

### 2. Why Dapr Actors?

Dapr actors provide:
- **Built-in State Management**: Automatic persistence and recovery
- **Virtual Actor Pattern**: Scalable and fault-tolerant
- **Consistent State**: Single-threaded execution model prevents race conditions
- **Automatic Lifecycle**: Activation, deactivation, and garbage collection

## Architecture Components

### 1. Snapshot Interfaces

```typescript
// Core snapshot data structure
interface AggregateSnapshot<TPayload extends IAggregatePayload> {
  aggregateId: string;
  partitionKey: PartitionKeys;
  payload: TPayload;
  version: number;
  lastEventId: string;
  lastEventTimestamp: Date;
  snapshotTimestamp: Date;
}

// Snapshot strategy interface
interface ISnapshotStrategy {
  shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number,
    lastSnapshotTime: Date | null
  ): boolean;
}

// Dapr actor interface for aggregate management
interface IAggregateActor<TPayload extends IAggregatePayload> {
  // Get current state (from snapshot or event replay)
  getState(): Promise<AggregateSnapshot<TPayload>>;
  
  // Apply new events and potentially create snapshot
  applyEvents(events: EventDocument[]): Promise<void>;
  
  // Force snapshot creation
  createSnapshot(): Promise<void>;
}
```

### 2. Snapshot Strategies

#### Count-Based Strategy
```typescript
class CountBasedSnapshotStrategy implements ISnapshotStrategy {
  constructor(private readonly threshold: number) {}
  
  shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number
  ): boolean {
    return (eventCount - lastSnapshotEventCount) >= this.threshold;
  }
}
```

#### Time-Based Strategy
```typescript
class TimeBasedSnapshotStrategy implements ISnapshotStrategy {
  constructor(private readonly intervalMs: number) {}
  
  shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number,
    lastSnapshotTime: Date | null
  ): boolean {
    if (!lastSnapshotTime) return true;
    return Date.now() - lastSnapshotTime.getTime() >= this.intervalMs;
  }
}
```

#### Hybrid Strategy
```typescript
class HybridSnapshotStrategy implements ISnapshotStrategy {
  constructor(
    private readonly eventThreshold: number,
    private readonly timeIntervalMs: number
  ) {}
  
  shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number,
    lastSnapshotTime: Date | null
  ): boolean {
    const countTriggered = (eventCount - lastSnapshotEventCount) >= this.eventThreshold;
    const timeTriggered = !lastSnapshotTime || 
      (Date.now() - lastSnapshotTime.getTime() >= this.timeIntervalMs);
    
    return countTriggered || timeTriggered;
  }
}
```

### 3. Dapr Actor Implementation

```typescript
// Base class for Dapr aggregate actors
abstract class DaprAggregateActor<TPayload extends IAggregatePayload> 
  extends Actor implements IAggregateActor<TPayload> {
  
  private snapshot?: AggregateSnapshot<TPayload>;
  private projector: IProjector<TPayload>;
  private snapshotStrategy: ISnapshotStrategy;
  
  constructor(
    host: ActorHost,
    projector: IProjector<TPayload>,
    snapshotStrategy: ISnapshotStrategy
  ) {
    super(host);
    this.projector = projector;
    this.snapshotStrategy = snapshotStrategy;
  }
  
  async onActivate(): Promise<void> {
    // Load snapshot from Dapr state
    this.snapshot = await this.stateManager.get<AggregateSnapshot<TPayload>>('snapshot');
  }
  
  async getState(): Promise<AggregateSnapshot<TPayload>> {
    if (!this.snapshot) {
      // Rebuild from events if no snapshot
      return this.rebuildFromEvents();
    }
    
    // Check for new events since snapshot
    const newEvents = await this.loadEventsSince(this.snapshot.lastEventId);
    if (newEvents.length > 0) {
      this.snapshot = await this.applyEventsToSnapshot(this.snapshot, newEvents);
    }
    
    return this.snapshot;
  }
  
  async applyEvents(events: EventDocument[]): Promise<void> {
    const currentState = await this.getState();
    const newState = await this.applyEventsToSnapshot(currentState, events);
    
    // Check if we should take a snapshot
    if (this.snapshotStrategy.shouldTakeSnapshot(
      newState.version,
      this.snapshot?.version || 0,
      this.snapshot?.snapshotTimestamp || null
    )) {
      await this.createSnapshot();
    }
  }
  
  async createSnapshot(): Promise<void> {
    const currentState = await this.getState();
    this.snapshot = {
      ...currentState,
      snapshotTimestamp: new Date()
    };
    
    // Save to Dapr state
    await this.stateManager.set('snapshot', this.snapshot);
  }
}
```

### 4. Integration with SekibanExecutor

The existing `SekibanExecutor` will be enhanced to work with Dapr actors:

```typescript
class DaprSekibanExecutor extends SekibanExecutor {
  private actorProxyFactory: IActorProxyFactory;
  
  async commandAsync<TCommand extends ICommandCommon>(
    command: TCommand
  ): Promise<ResultBox<CommandExecutionResult>> {
    // Get or create actor for the aggregate
    const actor = this.actorProxyFactory.createActorProxy<IAggregateActor>(
      ActorId.fromString(command.partitionKeys.aggregateId),
      'AggregateActor'
    );
    
    // Execute command through actor
    const currentState = await actor.getState();
    const result = await this.executeCommand(command, currentState);
    
    if (result.isOk()) {
      await actor.applyEvents(result.value.events);
    }
    
    return result;
  }
}
```

## Event Replay Optimization

With snapshots stored in Dapr actors, event replay is optimized:

1. **On Actor Activation**: Load snapshot from Dapr state
2. **Query for New Events**: Only load events after `lastEventId`
3. **Apply Delta**: Project only the new events onto the snapshot
4. **Lazy Snapshot Creation**: Use strategies to determine when to persist

## Configuration

```typescript
interface SnapshotConfiguration {
  strategy: 'count' | 'time' | 'hybrid' | 'custom';
  countThreshold?: number;        // For count-based strategy
  timeIntervalMs?: number;         // For time-based strategy
  customStrategy?: ISnapshotStrategy; // For custom implementations
}

// Example configuration
const snapshotConfig: SnapshotConfiguration = {
  strategy: 'hybrid',
  countThreshold: 100,    // Snapshot every 100 events
  timeIntervalMs: 3600000 // Or every hour
};
```

## Testing Strategy

### 1. Unit Tests
- Snapshot strategy logic
- Serialization/deserialization
- Event replay calculations

### 2. Integration Tests
- Dapr actor state persistence
- Snapshot creation and recovery
- Performance benchmarks

### 3. Test Scenarios
```typescript
describe('Snapshot Management', () => {
  it('should create snapshot based on count strategy', async () => {
    // Apply 100 events and verify snapshot creation
  });
  
  it('should recover from snapshot correctly', async () => {
    // Create snapshot, restart actor, verify state
  });
  
  it('should handle concurrent event application', async () => {
    // Test actor single-threaded guarantee
  });
});
```

## Performance Considerations

1. **Snapshot Size**: Monitor and potentially compress large snapshots
2. **Replay Optimization**: Minimize events loaded after snapshot
3. **Actor Memory**: Configure Dapr actor idle timeout appropriately
4. **Snapshot Frequency**: Balance between storage and replay performance

## Migration Path

For existing Sekiban users:
1. Snapshots remain optional (can run without them)
2. Gradual adoption through configuration
3. Backward compatibility with event-only systems

## Future Enhancements

1. **Snapshot Compression**: Add compression for large aggregates
2. **Snapshot Versioning**: Support multiple snapshot formats
3. **Distributed Snapshots**: For very large aggregates
4. **Snapshot Analytics**: Monitor snapshot effectiveness

## Summary

By leveraging Dapr actors for snapshot management, Sekiban TypeScript achieves:
- **Simplified Architecture**: No need for snapshot storage in event stores
- **Better Scalability**: Virtual actor pattern handles large numbers of aggregates
- **Improved Performance**: Reduced event replay overhead
- **Operational Simplicity**: Dapr handles state persistence and recovery