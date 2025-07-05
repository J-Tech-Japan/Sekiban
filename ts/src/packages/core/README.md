# @sekiban/core

Core event sourcing and CQRS framework for TypeScript, featuring a modern schema-based type registry system with runtime validation.

## Features

- **Schema-Based Type System**: Define events, commands, and projectors using Zod schemas
- **Runtime Validation**: Automatic validation at serialization boundaries
- **Type Safety**: Full TypeScript inference from schema definitions
- **Centralized Registry**: All executors use the same SekibanDomainTypes interface
- **Multiple Storage Options**: In-memory, Cosmos DB, PostgreSQL support
- **Distributed Systems Ready**: Dapr integration for actors and pub/sub

## Quick Start (Schema-Based Approach)

### 1. Define Events

```typescript
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1),
    email: z.string().email(),
    createdAt: z.string().datetime()
  })
});
```

### 2. Define Commands

```typescript
import { defineCommand, PartitionKeys, ok, err } from '@sekiban/core';

const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string().min(1),
    email: z.string().email()
  }),
  aggregateType: 'User', // Explicit aggregate type for routing
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('User'),
    validate: (data) => {
      // Business validation beyond schema
      if (data.email.endsWith('@test.com')) {
        return err({ type: 'ValidationError', message: 'Test emails not allowed' });
      }
      return ok(undefined);
    },
    handle: (data, aggregate) => {
      if (aggregate.payload.aggregateType !== 'Empty') {
        return err({ type: 'AggregateAlreadyExists', message: 'User already exists' });
      }
      return ok([
        UserCreated.create({
          userId: crypto.randomUUID(),
          name: data.name,
          email: data.email,
          createdAt: new Date().toISOString()
        })
      ]);
    }
  }
});
```

### 3. Define Projectors

```typescript
import { defineProjector, EmptyAggregatePayload } from '@sekiban/core';

interface UserPayload {
  aggregateType: 'User';
  userId: string;
  name: string;
  email: string;
  createdAt: string;
}

const userProjector = defineProjector<UserPayload | EmptyAggregatePayload>({
  aggregateType: 'User',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    UserCreated: (state, event: ReturnType<typeof UserCreated.create>) => ({
      aggregateType: 'User' as const,
      userId: event.userId,
      name: event.name,
      email: event.email,
      createdAt: event.createdAt
    }),
    UserUpdated: (state, event) => {
      if (state.aggregateType !== 'User') return state;
      return {
        ...state,
        name: event.name || state.name,
        email: event.email || state.email
      };
    }
  }
});
```

### 4. Register Types and Create Executor

```typescript
import { 
  globalRegistry, 
  createSchemaDomainTypes,
  createInMemorySekibanExecutor 
} from '@sekiban/core';

// Register all domain types
globalRegistry.registerEvent(UserCreated);
globalRegistry.registerCommand(CreateUser);
globalRegistry.registerProjector(userProjector);

// Create SekibanDomainTypes instance
const domainTypes = createSchemaDomainTypes(globalRegistry);

// Create executor with domain types
const executor = createInMemorySekibanExecutor(domainTypes);

// Execute commands
const command = CreateUser.create({
  name: 'John Doe',
  email: 'john@example.com'
});

const result = await executor.executeCommand(command);
```

## Migration from Class-Based System

See [Migration Guide](../../docs/migration-guide.md) for detailed instructions on migrating from the previous class-based system.

## Implementation Status

Successfully implemented using t_wada's TDD approach (Red-Green-Refactor).

### Phase 1: Base Utilities ✅

#### 1.1 Date Producer ✓
- `ISekibanDateProducer` interface
- `SekibanDateProducer` implementation with singleton pattern
- Mock implementations for testing
- Full test coverage (9 tests)

#### 1.2 UUID Extensions ✓
- `createVersion7()` - Time-ordered UUID v7 implementation
- `createNamespacedUuid()` - Deterministic UUID generation
- `generateUuid()` - Standard UUID v4
- `isValidUuid()` - UUID validation
- Full test coverage including edge cases (19 tests)

#### 1.3 Validation Utilities ✓
- Zod-based validation framework
- `createValidator()` - Create validators from Zod schemas
- `isValid()`, `getErrors()`, `validateOrThrow()` helper functions
- Support for complex validation scenarios
- Full test coverage (17 tests)

### Phase 2: Core Document Types ✅

#### 2.1 SortableUniqueId ✓
- Time-ordered unique identifier for events
- Sortable string representation
- Counter for rapid generation
- Result-based error handling
- Full test coverage (8 tests)

#### 2.2 PartitionKeys ✓
- Aggregate ID management
- Group and multi-tenant support
- Composite key generation
- Equality comparison
- Full test coverage (13 tests)

#### 2.3 Metadata ✓
- Command and event metadata
- User ID, correlation ID, causation ID tracking
- Custom metadata support
- Builder pattern implementation
- Full test coverage (24 tests)

### Phase 3: Basic Interfaces ✅

#### 3.1 Event Basic Types ✓
- `IEventPayload` - Marker interface for event payloads
- `IAggregatePayload` - Marker interface for aggregate states
- Type guards for runtime validation
- Full test coverage (17 tests)

#### 3.2 Event Type Definitions ✓
- `IEvent<TPayload>` - Core event interface
- `Event` class with immutability
- `EventMetadata` with correlation/causation support
- Event creation helpers
- Full test coverage (10 tests)

### Phase 4: Event Management ✅

#### 4.1 Event Document ✓
- `EventDocument` - Wrapper for events with convenience accessors
- `SerializableEventDocument` - JSON-serializable event representation
- Serialization/deserialization utilities
- Full test coverage (9 tests)

#### 4.2 In-Memory Event Store ✓
- `InMemoryEventStore` - Event storage implementation
- `IEventReader` & `IEventWriter` interfaces
- Version consistency enforcement
- Multi-tenant support
- Full test coverage (13 tests)

### Phase 5: Exception and Error Handling ✅

#### 5.1 Sekiban Error Classes ✓
- Base `SekibanError` class with serialization support
- Domain-specific error types (10 error classes)
- Full error inheritance hierarchy
- JSON serialization support
- Full test coverage (20 tests)

#### 5.2 Error Utilities ✓
- `toResult()` - Convert promises to Results
- `fromThrowable()` - Wrap throwing functions
- `mapError()` - Transform error types
- `collectErrors()` - Gather errors from Results
- `chainErrors()` - Create error cause chains
- Full test coverage (15 tests)

#### 5.3 Error Type Guards ✓
- Type-safe error checking functions
- Support for all Sekiban error types
- TypeScript type narrowing
- Composable guards
- Full test coverage (13 tests)

### Phase 6: Aggregates and Projectors ✅

#### 6.1 Aggregate System ✓
- `IAggregate` interface with version tracking
- `Aggregate` class with immutability
- Empty aggregate support
- Version management and event tracking
- Full test coverage (8 tests)

#### 6.2 Projector System ✓
- `IAggregateProjector` - Pure projection functions
- `IProjector` - Extended with type information
- Pattern matching for state transitions
- Event-driven state updates
- Full test coverage (9 tests)

#### 6.3 Aggregate Projector ✓
- Wrapper for applying projectors to aggregates
- Sequential event application
- Initial aggregate creation
- Error handling
- Full test coverage (6 tests)

### Phase 7: Command Handling ✅

#### 7.1 Command Interfaces ✓
- `ICommand` marker interface
- `ICommandHandler` for command processing
- `ICommandWithHandler` combining command and handler
- Command context with aggregate access
- Full test coverage (7 tests)

#### 7.2 Command Validation ✓
- Declarative validation rules
- Built-in validators (required, minLength, email, etc.)
- Custom validation support
- Validation error collection
- Full test coverage (9 tests)

### Phase 8: Query Processing ✅

#### 8.1 Query Interfaces ✓
- `IQuery` base interface
- `IMultiProjectionQuery` for single results
- `IMultiProjectionListQuery` for lists with filtering/sorting
- Query context for dependency injection
- Full test coverage (9 tests)

#### 8.2 Multi-Projections ✓
- `IMultiProjector` interface
- `MultiProjectionState` container
- `AggregateListProjector` for aggregate queries
- Event-driven state updates
- Full test coverage (8 tests)

### Phase 9: SekibanExecutor ✅

#### 9.1 Executor Interfaces ✓
- `ISekibanExecutor` - Main execution interface
- `ICommandExecutor` - Command execution specialization
- `IQueryExecutor` - Query execution specialization
- Type-safe command and query execution
- Full test coverage (6 tests)

#### 9.2 InMemorySekibanExecutor ✓
- Complete in-memory executor implementation
- Command execution with validation and event storage
- Query execution with aggregate projection
- Multi-projection query support
- Configuration and error handling
- Full test coverage (12 tests)

### Phase 10: Storage Provider Integration ✅

#### 10.1 Storage Provider Interfaces ✓
- `IEventStorageProvider` - Base storage interface
- `StorageProviderConfig` - Configuration settings
- `EventBatch` - Batch event operations
- `SnapshotData` - Snapshot management
- Full test coverage (5 tests)

#### 10.2 Storage Errors ✓
- `StorageError` - Base storage error
- `ConnectionError` - Connection failures
- `ConcurrencyError` - Version conflicts
- Error hierarchy with proper inheritance
- Full test coverage (3 tests)

#### 10.3 InMemoryStorageProvider ✓
- Complete in-memory implementation
- Optimistic concurrency control
- Snapshot support
- Event loading with filtering
- Full test coverage (6 tests)

#### 10.4 Storage Provider Factory ✓
- Dynamic provider registration
- Built-in provider support (InMemory, CosmosDB, PostgreSQL)
- Custom provider registration
- Configuration-based provider creation
- Full test coverage (5 tests)

#### 10.5 Placeholder Providers ✓
- `CosmosStorageProvider` - Azure Cosmos DB placeholder
- `PostgresStorageProvider` - PostgreSQL placeholder
- Ready for full implementation with respective SDKs
- Full test coverage (3 tests)

**Total Tests: 324 passing** ✅

## Usage

```typescript
import { 
  SekibanDateProducer,
  createVersion7,
  createValidator,
  SortableUniqueId,
  PartitionKeys,
  Metadata,
  MetadataBuilder,
  z
} from '@sekiban/core';

// Date producer
const dateProducer = SekibanDateProducer.getRegistered();
const now = dateProducer.now();

// UUID v7 (time-ordered)
const aggregateId = createVersion7();

// Validation
const userSchema = z.object({
  name: z.string().min(1),
  email: z.string().email()
});

const validator = createValidator(userSchema);
const result = validator.validate({ name: 'John', email: 'john@example.com' });

if (result.success) {
  console.log('Valid user:', result.data);
}

// Sortable Unique ID
const eventId = SortableUniqueId.generate();
console.log('Event ID:', eventId.toString());

// Partition Keys
const partitionKeys = PartitionKeys.create('user-123', 'users', 'tenant-1');
console.log('Partition Key:', partitionKeys.toString()); // "tenant-1-users-user-123"

// Metadata
const metadata = new MetadataBuilder()
  .withUserId('user-123')
  .withCorrelationId('request-456')
  .withCustom('source', 'web-api')
  .build();

// Error handling with neverthrow
import { toResult, isEventStoreError } from '@sekiban/core';

const result = await toResult(fetchData());
if (result.isErr()) {
  if (isEventStoreError(result.error)) {
    console.error('Event store error:', result.error.operation);
  }
}

// Aggregates and Projectors
import { 
  IProjector, 
  IAggregatePayload, 
  IEventPayload,
  AggregateProjector,
  createEmptyAggregate
} from '@sekiban/core';

// Define your projector
class AccountProjector implements IProjector<IAggregatePayload> {
  getTypeName() { return 'AccountProjector'; }
  getVersion() { return 1; }
  
  project(state: IAggregatePayload, event: IEventPayload): IAggregatePayload {
    // Pattern matching logic here
    return state;
  }
}

// Use the projector
const projector = new AggregateProjector(new AccountProjector());
const aggregate = projector.getInitialAggregate(partitionKeys, 'Account');

// Commands
import { 
  ICommandWithHandler,
  validateCommand,
  required,
  email as emailValidator
} from '@sekiban/core';

class CreateUserCommand implements ICommandWithHandler<CreateUserCommand, UserProjector> {
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {}
  
  validate() {
    return validateCommand(this, {
      name: [required('Name is required')],
      email: [required('Email is required'), emailValidator('Invalid email')]
    });
  }
  
  getPartitionKeys() {
    return PartitionKeys.generate('users');
  }
  
  handle(command: CreateUserCommand, context: ICommandContextWithoutState) {
    const event = new UserCreated(command.name, command.email);
    return ok(EventOrNone.event(context.createEvent(event)));
  }
}

// Queries
import {
  IMultiProjectionQuery,
  MultiProjectionState,
  AggregateListProjector,
  createAggregateListProjector
} from '@sekiban/core';

// Executors
import {
  ISekibanExecutor,
  InMemorySekibanExecutor,
  InMemoryEventStore
} from '@sekiban/core';

// Create aggregate list projector
const userListProjector = createAggregateListProjector(new UserProjector());

// Define a query
class GetUserByIdQuery implements IMultiProjectionQuery<
  typeof userListProjector,
  GetUserByIdQuery,
  UserDto
> {
  constructor(public readonly userId: string) {}
  
  static handleQuery(
    projection: MultiProjectionState<typeof userListProjector>,
    query: GetUserByIdQuery,
    context: IQueryContext
  ) {
    const aggregate = projection.payload.aggregates.get(query.userId);
    if (!aggregate) {
      return err(new QueryExecutionError('GetUserByIdQuery', 'User not found'));
    }
    
    const user = aggregate.payload as UserPayload;
    return ok({ id: user.id, name: user.name, email: user.email });
  }
}

// Using the executor
const eventStore = new InMemoryEventStore();
const userProjector = new UserProjector();
const executor = new InMemorySekibanExecutor({
  eventStore,
  projectors: [userProjector]
});

// Execute commands
const createCommand = new CreateUserCommand('John Doe', 'john@example.com');
const result = await executor.commandAsync(createCommand);

if (result.isOk()) {
  console.log('User created:', result.value.aggregateId);
}

// Execute queries
const query = new GetUserByIdQuery(result.value.aggregateId);
const queryResult = await executor.queryAsync(query, userProjector);

if (queryResult.isOk()) {
  console.log('User found:', queryResult.value.data);
}

// Storage Providers
import {
  StorageProviderType,
  StorageProviderConfig,
  StorageProviderFactory,
  EventBatch
} from '@sekiban/core';

// Configure storage provider
const storageConfig: StorageProviderConfig = {
  type: StorageProviderType.InMemory,
  maxRetries: 3,
  retryDelayMs: 100
};

// Create storage provider
const providerResult = await StorageProviderFactory.create(storageConfig);
if (providerResult.isOk()) {
  const provider = providerResult.value;
  
  // Initialize provider
  await provider.initialize();
  
  // Save events
  const batch: EventBatch = {
    partitionKeys,
    events: [event1, event2],
    expectedVersion: 0
  };
  
  const saveResult = await provider.saveEvents(batch);
  if (saveResult.isErr()) {
    console.error('Failed to save events:', saveResult.error);
  }
  
  // Load events
  const loadResult = await provider.loadEventsByPartitionKey(partitionKeys);
  if (loadResult.isOk()) {
    console.log('Loaded events:', loadResult.value);
  }
  
  // Close provider
  await provider.close();
}
```

## Testing

```bash
pnpm test
```

## Building

```bash
pnpm build
```