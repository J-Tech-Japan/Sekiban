# @sekiban/dapr

> ⚠️ **Alpha Version**: This package is currently in alpha. APIs may change between releases.

Dapr actor integration for Sekiban Event Sourcing framework with snapshot support.

## Overview

This package provides integration between Sekiban and Dapr actors, enabling:
- Automatic snapshot management using Dapr actor state
- Optimized event replay with snapshot support
- Configurable snapshot strategies
- Virtual actor pattern for scalability

## Installation

```bash
npm install @sekiban/dapr@alpha @sekiban/core@alpha
# or
pnpm add @sekiban/dapr@alpha @sekiban/core@alpha
# or
yarn add @sekiban/dapr@alpha @sekiban/core@alpha
```

## Features

### Snapshot Management
- **Actor State Storage**: Snapshots are stored in Dapr actor state, separate from events
- **Configurable Strategies**: Count-based, time-based, hybrid, or custom strategies
- **Automatic Optimization**: Reduce event replay overhead with smart snapshot placement
- **Compression Support**: Future support for snapshot compression

### Actor Model
- **Virtual Actors**: Leverage Dapr's virtual actor pattern
- **Single-threaded Execution**: Guaranteed consistency within each aggregate
- **Automatic Lifecycle**: Actors activate/deactivate as needed
- **State Persistence**: Automatic state management through Dapr

## Usage

### Basic Setup

```typescript
import { DaprClient } from '@dapr/dapr';
import { DaprAggregateActor, ISnapshotStrategy } from '@sekiban/dapr';
import type { IProjector, IAggregatePayload } from '@sekiban/core';

// Define your aggregate payload
interface UserAggregate extends IAggregatePayload {
  name: string;
  email: string;
}

// Define your projector
class UserProjector implements IProjector<UserAggregate> {
  initialState(): UserAggregate {
    return { name: '', email: '' };
  }

  applyEvent(state: UserAggregate, event: EventDocument): UserAggregate {
    switch (event.payload.type) {
      case 'UserCreated':
        return { name: event.payload.name, email: event.payload.email };
      case 'UserUpdated':
        return { ...state, ...event.payload.updates };
      default:
        return state;
    }
  }
}

// Create your actor
class UserActor extends DaprAggregateActor<UserAggregate> {
  constructor(host: ActorHost) {
    super(
      host,
      eventStore,
      new UserProjector(),
      ISnapshotStrategy.fromConfig({
        strategy: 'hybrid',
        countThreshold: 100,
        timeIntervalMs: 3600000, // 1 hour
      })
    );
  }
}
```

### Snapshot Strategies

#### Count-Based Strategy
```typescript
// Snapshot every 100 events
const strategy = new CountBasedSnapshotStrategy(100);
```

#### Time-Based Strategy
```typescript
// Snapshot every hour
const strategy = new TimeBasedSnapshotStrategy(60 * 60 * 1000);
```

#### Hybrid Strategy
```typescript
// Snapshot every 50 events OR every 30 minutes
const strategy = new HybridSnapshotStrategy(50, 30 * 60 * 1000);
```

#### No Snapshot Strategy
```typescript
// Disable snapshots
const strategy = new NoSnapshotStrategy();
```

### Configuration

```typescript
const snapshotConfig: SnapshotConfiguration = {
  strategy: 'hybrid',
  countThreshold: 100,
  timeIntervalMs: 3600000, // 1 hour
  enableCompression: true, // Future feature
  compressionAlgorithm: 'gzip',
  compressionThreshold: 1024, // Compress if > 1KB
};
```

## Architecture

### Separation of Concerns
- **Events**: Stored in PostgreSQL, CosmosDB, or in-memory stores
- **Snapshots**: Stored in Dapr actor state
- **Actors**: Manage aggregate lifecycle and snapshot strategy

### Performance Optimization
1. On actor activation: Load snapshot from state
2. Query only new events since snapshot
3. Apply delta events to rebuild current state
4. Create new snapshots based on strategy

### Multi-tenancy Support
Actor IDs include tenant information for proper isolation:
```typescript
// Single tenant
ActorId: "user-123"

// Multi-tenant
ActorId: "tenant1:user-123"
```

## Testing

The package includes comprehensive test utilities:

```typescript
import { DaprAggregateActor } from '@sekiban/dapr';
import { MockStateManager, MockEventStore } from '@sekiban/dapr/testing';

// Test your actors with mock implementations
const actor = new TestActor(
  mockHost,
  mockEventStore,
  projector,
  strategy
);
```

## Best Practices

1. **Snapshot Frequency**: Balance between storage and replay performance
2. **Event Store Choice**: Use PostgreSQL or CosmosDB for production
3. **Actor Timeout**: Configure based on your aggregate access patterns
4. **Monitoring**: Track snapshot effectiveness and replay times

## Future Enhancements

- Snapshot compression algorithms
- Distributed snapshots for very large aggregates
- Snapshot versioning and migration
- Performance metrics and monitoring

## License

MIT