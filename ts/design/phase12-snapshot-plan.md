# Phase 12: Snapshot Management Implementation Plan

## Overview
Implement snapshot management using Dapr actors for state persistence, while keeping event storage in PostgreSQL, CosmosDB, or in-memory stores.

## Key Principles
1. **Separation of Concerns**: Events in event stores, snapshots in Dapr actor state
2. **TDD Approach**: Write tests first, then implementation
3. **Incremental Development**: Build features step by step
4. **Actor Model**: Leverage Dapr's virtual actor pattern

## Implementation Steps

### Step 1: Core Snapshot Types and Interfaces
**Goal**: Define the foundational types for snapshot management

#### Tests First (TDD):
```typescript
// packages/core/src/snapshot/__tests__/snapshot-types.test.ts
- Test snapshot data structure serialization
- Test snapshot metadata handling
- Test version compatibility
```

#### Implementation:
```typescript
// packages/core/src/snapshot/types.ts
- AggregateSnapshot<T> interface
- SnapshotMetadata type
- SnapshotVersion handling
```

### Step 2: Snapshot Strategies
**Goal**: Implement configurable strategies for when to take snapshots

#### Tests First (TDD):
```typescript
// packages/core/src/snapshot/__tests__/snapshot-strategies.test.ts
- Test count-based strategy triggers
- Test time-based strategy triggers
- Test hybrid strategy logic
- Test custom strategy interface
```

#### Implementation:
```typescript
// packages/core/src/snapshot/strategies/
- ISnapshotStrategy interface
- CountBasedSnapshotStrategy
- TimeBasedSnapshotStrategy
- HybridSnapshotStrategy
- NoSnapshotStrategy (opt-out option)
```

### Step 3: Dapr Actor Base Implementation
**Goal**: Create base actor class for aggregate management

#### Tests First (TDD):
```typescript
// packages/dapr/src/__tests__/aggregate-actor.test.ts
- Test actor activation and state loading
- Test snapshot creation
- Test event replay from snapshot
- Test state persistence
```

#### Implementation:
```typescript
// packages/dapr/src/actors/aggregate-actor.ts
- DaprAggregateActor base class
- State management methods
- Snapshot lifecycle methods
```

### Step 4: Event Replay Optimization
**Goal**: Optimize event loading when snapshots exist

#### Tests First (TDD):
```typescript
// packages/core/src/snapshot/__tests__/event-replay.test.ts
- Test loading events after snapshot
- Test applying events to snapshot
- Test handling missing snapshots
- Test version conflict detection
```

#### Implementation:
```typescript
// packages/core/src/snapshot/event-replay.ts
- EventReplayOptimizer
- Delta event loading
- Snapshot + events merging
```

### Step 5: Integration with SekibanExecutor
**Goal**: Enhance executor to work with Dapr actors

#### Tests First (TDD):
```typescript
// packages/dapr/src/__tests__/dapr-executor.test.ts
- Test command execution through actors
- Test query handling with snapshots
- Test concurrent command handling
- Test actor lifecycle management
```

#### Implementation:
```typescript
// packages/dapr/src/executor/dapr-sekiban-executor.ts
- DaprSekibanExecutor extends SekibanExecutor
- Actor proxy management
- Command routing to actors
```

### Step 6: Configuration and Factory
**Goal**: Provide configuration options for snapshot behavior

#### Tests First (TDD):
```typescript
// packages/core/src/snapshot/__tests__/snapshot-config.test.ts
- Test configuration validation
- Test strategy factory
- Test default configurations
```

#### Implementation:
```typescript
// packages/core/src/snapshot/config/
- SnapshotConfiguration interface
- SnapshotStrategyFactory
- Default configurations
```

### Step 7: Testing Utilities
**Goal**: Create test helpers for snapshot scenarios

#### Tests First (TDD):
```typescript
// packages/testing/src/__tests__/snapshot-testing.test.ts
- Test snapshot test helpers
- Test mock actor implementations
- Test snapshot assertions
```

#### Implementation:
```typescript
// packages/testing/src/snapshot/
- SnapshotTestBase class
- Mock Dapr actor helpers
- Snapshot assertion utilities
```

## Package Structure

```
packages/
├── core/
│   └── src/
│       └── snapshot/
│           ├── types.ts
│           ├── strategies/
│           ├── event-replay.ts
│           └── config/
├── dapr/
│   └── src/
│       ├── actors/
│       │   └── aggregate-actor.ts
│       └── executor/
│           └── dapr-sekiban-executor.ts
└── testing/
    └── src/
        └── snapshot/
```

## Testing Strategy

### Unit Tests
- Pure logic testing (strategies, calculations)
- No external dependencies
- Fast execution

### Integration Tests
- Dapr actor state persistence
- Event store integration
- End-to-end scenarios

### Performance Tests
- Measure snapshot vs. no-snapshot performance
- Event replay optimization metrics
- Memory usage analysis

## Example Usage

```typescript
// Configure snapshot strategy
const snapshotConfig: SnapshotConfiguration = {
  strategy: 'hybrid',
  countThreshold: 50,
  timeIntervalMs: 60000 // 1 minute
};

// Create Dapr-enabled executor
const executor = new DaprSekibanExecutor({
  eventStore: new PostgresEventStore(config),
  snapshotConfig,
  actorSystem: daprClient
});

// Commands automatically use actors with snapshots
const result = await executor.commandAsync({
  type: 'CreateUser',
  payload: { name: 'John' },
  partitionKeys: PartitionKeys.generate<UserAggregate>()
});
```

## Success Criteria

1. **All tests passing**: 100% test coverage for new code
2. **Performance improvement**: Measurable reduction in event replay time
3. **Backward compatibility**: Works with existing event stores
4. **Developer experience**: Simple configuration and usage
5. **Documentation**: Clear examples and guides

## Risks and Mitigations

### Risk 1: Dapr Dependency
- **Mitigation**: Make Dapr optional, allow pure event sourcing

### Risk 2: Snapshot Size
- **Mitigation**: Plan for compression in future phases

### Risk 3: Version Conflicts
- **Mitigation**: Clear versioning strategy and conflict detection

## Timeline Estimate

- **Week 1**: Core types, interfaces, and strategies
- **Week 2**: Dapr actor implementation
- **Week 3**: Integration and testing
- **Week 4**: Documentation and examples

## Next Steps After Phase 12

1. **Phase 13**: Event Versioning & Schema Evolution
2. **Phase 14**: Testing Framework & DevEx Tools
3. **Phase 15**: Process Managers & Sagas

## Notes

- Consult ChatGPT via MCP for Dapr best practices
- Follow TDD strictly: Red → Green → Refactor
- Keep actor logic simple and testable
- Consider future compression needs in design